using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

/// <summary>Pending approvals the current user can decide now: as an approver, or as an active delegate.</summary>
public sealed record GetApprovalInboxQuery : IQuery<IReadOnlyList<InboxItemDto>>;

internal sealed class GetApprovalInboxQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetApprovalInboxQuery, IReadOnlyList<InboxItemDto>>
{
    public async Task<Result<IReadOnlyList<InboxItemDto>>> Handle(GetApprovalInboxQuery query, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        var today = clock.Today;
        var delegators = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => d.ToUserId == userId && !d.IsRevoked && d.ValidFrom <= today && d.ValidTo >= today)
            .Select(d => d.FromUserId)
            .ToListAsync(cancellationToken);
        var actingFor = delegators.Append(userId).ToList();

        // The JSON array of eligible approvers is searched in SQL (OPENJSON).
        var pending = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending && r.RequestedBy != userId)
            .Where(r => r.Steps.Any(s => s.Status == ApprovalStepStatus.Pending && s.EligibleApproverIds.Any(id => actingFor.Contains(id))))
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        return pending
            .Select(r =>
            {
                var step = r.CurrentStep!;
                return new InboxItemDto(r.Id, r.DocumentType, r.DocumentId, r.DocumentTitle, r.Amount, step.Name, r.RequestedAtUtc,
                    AsDelegate: !step.EligibleApproverIds.Contains(userId));
            })
            .ToList();
    }
}

public sealed record GetApprovalQuery(Guid ApprovalRequestId) : IQuery<ApprovalDto>;

internal sealed class GetApprovalQueryHandler(IAppDbContext db) : IQueryHandler<GetApprovalQuery, ApprovalDto>
{
    public async Task<Result<ApprovalDto>> Handle(GetApprovalQuery query, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == query.ApprovalRequestId, cancellationToken);
        return request is null ? ApprovalErrors.NotFound : ApprovalMapping.ToDto(request);
    }
}
