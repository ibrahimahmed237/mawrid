using FluentValidation;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

/// <param name="ExpectedVersion">Optional: the version the approver saw (from the If-Match header). A different current version gives 412.</param>
public sealed record DecideApprovalCommand(Guid ApprovalRequestId, bool Approve, string? Comment, string? ExpectedVersion) : ICommand;

internal sealed class DecideApprovalCommandValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalCommandValidator()
    {
        RuleFor(x => x.Comment).MaximumLength(ApprovalRequest.CommentMaxLength);
        RuleFor(x => x.Comment).NotEmpty().When(x => !x.Approve).WithMessage("A reason is required to reject.");
    }
}

internal sealed class DecideApprovalCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IEnumerable<IApprovalDocumentHandler> documentHandlers) : ICommandHandler<DecideApprovalCommand>
{
    public async Task<Result> Handle(DecideApprovalCommand command, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.SingleOrDefaultAsync(r => r.Id == command.ApprovalRequestId, cancellationToken);
        if (request is null)
        {
            return ApprovalErrors.NotFound;
        }

        if (RowVersions.IsStale(command.ExpectedVersion, request.RowVersion))
        {
            return ApprovalErrors.StaleVersion;
        }

        var userId = currentUser.UserId!.Value;
        var today = clock.Today;
        var delegations = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => d.ToUserId == userId && !d.IsRevoked && d.ValidFrom <= today && d.ValidTo >= today)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        var decided = command.Approve
            ? request.Approve(userId, delegations, command.Comment, now)
            : request.Reject(userId, delegations, command.Comment ?? string.Empty, now);
        if (decided.IsFailure)
        {
            return decided;
        }

        // When the approval is complete, the owning module updates its document in the same transaction.
        if (request.Status is ApprovalStatus.Approved or ApprovalStatus.Rejected)
        {
            var handler = documentHandlers.Single(h => h.DocumentType == request.DocumentType);
            var outcome = request.Status == ApprovalStatus.Approved
                ? await handler.OnApprovedAsync(request.DocumentId, cancellationToken)
                : await handler.OnRejectedAsync(request.DocumentId, command.Comment!, cancellationToken);
            if (outcome.IsFailure)
            {
                return outcome;
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApprovalErrors.ConcurrentDecision;
        }

        return Result.Success();
    }
}
