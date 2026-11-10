using Mawrid.Application.Common.Notifications;
using Microsoft.Extensions.Logging;

namespace Mawrid.Infrastructure.Notifications;

/// <summary>One way to reach a user: real-time push, email, and later SMS or Teams.</summary>
public interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken);
}

/// <summary>Sends a notification through every channel. A failing channel is logged and skipped.</summary>
internal sealed partial class NotificationSender(IEnumerable<INotificationChannel> channels, ILogger<NotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var channel in channels)
        {
            try
            {
                await channel.SendAsync(recipients, notification, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogChannelFailed(exception, channel.Name, notification.Type);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification channel {Channel} failed for {NotificationType}")]
    private partial void LogChannelFailed(Exception exception, string channel, string notificationType);
}
