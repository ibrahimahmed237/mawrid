namespace Mawrid.Application.Common.Notifications;

/// <summary>
/// Tells users that something happened (real-time push and email). Best effort: a failed channel
/// is logged and never fails the caller. Event handlers call it after the change is saved.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken);
}

/// <param name="Type">A stable code clients can switch on, for example <c>purchase_order.sent</c>.</param>
/// <param name="Path">The API path of the record, so a client can open it.</param>
public sealed record UserNotification(string Type, string Title, string Message, string? Path = null);
