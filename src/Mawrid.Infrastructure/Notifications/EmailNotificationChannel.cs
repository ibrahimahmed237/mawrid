using Mawrid.Application.Common.Notifications;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mawrid.Infrastructure.Notifications;

/// <summary>Sends email. Production plugs in a real provider (SMTP, SendGrid, ...) behind this interface.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

/// <summary>Development email: writes the message to the log instead of sending it.</summary>
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        LogEmail(to, subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email to {To}: {Subject}")]
    private partial void LogEmail(string to, string subject);
}

/// <summary>Emails each active recipient. Inactive users get nothing.</summary>
internal sealed class EmailNotificationChannel(AppDbContext db, IEmailSender email) : INotificationChannel
{
    public string Name => "email";

    public async Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken)
    {
        var addresses = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);

        foreach (var address in addresses)
        {
            await email.SendAsync(address, notification.Title, notification.Message, cancellationToken);
        }
    }
}
