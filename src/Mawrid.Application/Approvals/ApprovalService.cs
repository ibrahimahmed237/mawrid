using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Application.Contracts.Identity;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

internal sealed class ApprovalService(
    IAppDbContext db,
    IOrganizationDirectory directory,
    ICurrentUser currentUser,
    IClock clock) : IApprovalService
{
    public async Task<Result<Guid>> StartAsync(ApprovalStart start, CancellationToken cancellationToken)
    {
        var policy = await db.ApprovalPolicies.SingleOrDefaultAsync(p => p.DocumentType == start.DocumentType, cancellationToken);
        if (policy is null)
        {
            return ApprovalErrors.PolicyNotFound;
        }

        var rule = policy.RuleFor(start.Amount);
        var steps = new List<(ApprovalStepTemplate, IReadOnlyCollection<Guid>)>();
        foreach (var template in rule.Steps)
        {
            steps.Add((template, await EligibleApproversAsync(template, start.CostCenterId, cancellationToken)));
        }

        var started = ApprovalRequest.Start(currentUser.OrganizationId!.Value, start.DocumentType, start.DocumentId, start.DocumentTitle,
            start.Amount, start.CostCenterId, start.RequestedBy, steps, clock.UtcNow);
        if (started.IsFailure)
        {
            return started.Error!;
        }

        db.ApprovalRequests.Add(started.Value);
        return started.Value.Id;
    }

    public async Task CancelAsync(Guid approvalRequestId, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.SingleOrDefaultAsync(r => r.Id == approvalRequestId, cancellationToken);
        request?.Cancel(clock.UtcNow);
    }

    private async Task<IReadOnlyCollection<Guid>> EligibleApproversAsync(ApprovalStepTemplate template, Guid costCenterId, CancellationToken cancellationToken)
    {
        if (template.Kind == ApprovalStepKind.CostCenterApprovers)
        {
            return await directory.CostCenterApproversAsync(costCenterId, cancellationToken);
        }

        var members = await db.ApproverGroups.AsNoTracking()
            .Where(g => g.Id == template.GroupId)
            .Select(g => g.MemberUserIds)
            .SingleOrDefaultAsync(cancellationToken) ?? [];

        // Only members who are still active employees can approve.
        return await directory.ActiveInternalUsersAsync(members, cancellationToken);
    }
}
