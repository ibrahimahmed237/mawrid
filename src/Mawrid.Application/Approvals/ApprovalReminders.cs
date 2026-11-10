using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Messaging;
using Mawrid.Application.Common.Notifications;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Mawrid.Application.Approvals;

/// <summary>
/// System job, each working morning per organization: reminds approvers about steps that have
/// waited at least one day.
/// </summary>
public sealed record SendApprovalRemindersCommand : ICommand<int>;

internal sealed class SendApprovalRemindersCommandHandler(IAppDbContext db, ICurrentUser currentUser, INotificationSender notifications, IClock clock)
    : ICommandHandler<SendApprovalRemindersCommand, int>
{
    public static readonly TimeSpan WaitBeforeReminder = TimeSpan.FromDays(1);

    public async Task<Result<int>> Handle(SendApprovalRemindersCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsSystem)
        {
            return SystemErrors.SystemOnly;
        }

        var now = clock.UtcNow;
        var pending = await db.ApprovalRequests.AsNoTracking().Where(r => r.Status == ApprovalStatus.Pending).ToListAsync(cancellationToken);
        var reminded = 0;

        foreach (var request in pending)
        {
            var step = request.CurrentStep!;
            var waitingSince = WaitingSince(request, step);
            if (now - waitingSince < WaitBeforeReminder)
            {
                continue;
            }

            var recipients = await ApprovalRecipients.ForStepAsync(db, step, clock.Today, cancellationToken);
            await notifications.SendAsync(recipients, new UserNotification(
                "approval.reminder",
                $"Reminder: {request.DocumentTitle} waits for approval",
                $"{step.Name}: {request.DocumentTitle} has waited since {waitingSince:yyyy-MM-dd HH:mm} UTC.",
                $"/api/v1/approvals/{request.Id}"), cancellationToken);
            reminded++;
        }

        return reminded;
    }

    /// <summary>A step starts waiting when the step before it was decided, or when the request started.</summary>
    private static DateTimeOffset WaitingSince(ApprovalRequest request, ApprovalStep step) =>
        request.Steps.Where(s => s.Order < step.Order).Select(s => s.DecidedAtUtc).Max() ?? request.RequestedAtUtc;
}
