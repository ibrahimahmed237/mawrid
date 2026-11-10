using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Events;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Domain.Approvals;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

/// <summary>Tells approvers when a step waits for them, and the requester how the approval ended.</summary>
internal sealed class ApprovalNotifications(IAppDbContext db, INotificationSender notifications, IClock clock) :
    INotificationHandler<DomainEventNotification<ApprovalRequestStarted>>,
    INotificationHandler<DomainEventNotification<ApprovalStepActivated>>,
    INotificationHandler<DomainEventNotification<ApprovalRequestApproved>>,
    INotificationHandler<DomainEventNotification<ApprovalRequestRejected>>
{
    public Task Handle(DomainEventNotification<ApprovalRequestStarted> notification, CancellationToken cancellationToken) =>
        NotifyApproversAsync(notification.Event.ApprovalRequestId, stepOrder: 1, cancellationToken);

    public Task Handle(DomainEventNotification<ApprovalStepActivated> notification, CancellationToken cancellationToken) =>
        NotifyApproversAsync(notification.Event.ApprovalRequestId, notification.Event.StepOrder, cancellationToken);

    public Task Handle(DomainEventNotification<ApprovalRequestApproved> notification, CancellationToken cancellationToken) =>
        NotifyRequesterAsync(notification.Event.ApprovalRequestId, "approval.approved", "Approved", null, cancellationToken);

    public Task Handle(DomainEventNotification<ApprovalRequestRejected> notification, CancellationToken cancellationToken) =>
        NotifyRequesterAsync(notification.Event.ApprovalRequestId, "approval.rejected", "Rejected", notification.Event.Reason, cancellationToken);

    /// <summary>Notifies the step's approvers and their active delegates, if the step still waits.</summary>
    private async Task NotifyApproversAsync(Guid approvalRequestId, int stepOrder, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == approvalRequestId, cancellationToken);
        if (request?.CurrentStep is not { } step || step.Order != stepOrder)
        {
            return;
        }

        var recipients = await ApprovalRecipients.ForStepAsync(db, step, clock.Today, cancellationToken);
        await notifications.SendAsync(recipients, new UserNotification(
            "approval.waiting",
            $"Approval needed: {request.DocumentTitle}",
            $"{step.Name}: please review {request.DocumentTitle} ({request.Amount:N2} SAR).",
            $"/api/v1/approvals/{request.Id}"), cancellationToken);
    }

    private async Task NotifyRequesterAsync(Guid approvalRequestId, string type, string outcome, string? reason, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == approvalRequestId, cancellationToken);
        if (request is null)
        {
            return;
        }

        var message = reason is null ? $"{request.DocumentTitle} was approved." : $"{request.DocumentTitle} was rejected: {reason}";
        await notifications.SendAsync([request.RequestedBy], new UserNotification(
            type, $"{outcome}: {request.DocumentTitle}", message, $"/api/v1/approvals/{request.Id}"), cancellationToken);
    }
}

internal static class ApprovalRecipients
{
    /// <summary>The eligible approvers of a step plus anyone they delegated to for today.</summary>
    public static async Task<IReadOnlyCollection<Guid>> ForStepAsync(IAppDbContext db, ApprovalStep step, DateOnly today, CancellationToken cancellationToken)
    {
        var approvers = step.EligibleApproverIds;
        var delegates = await db.ApprovalDelegations.AsNoTracking()
            .Where(d => approvers.Contains(d.FromUserId) && !d.IsRevoked && d.ValidFrom <= today && d.ValidTo >= today)
            .Select(d => d.ToUserId)
            .ToListAsync(cancellationToken);

        return approvers.Concat(delegates).Distinct().ToList();
    }
}
