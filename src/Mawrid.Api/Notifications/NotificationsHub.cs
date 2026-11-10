using Mawrid.Application.Common.Notifications;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mawrid.Api.Notifications;

/// <summary>
/// Real-time notifications. Clients connect to <c>/hubs/notifications</c> with their access token
/// (as <c>?access_token=</c>, because browsers cannot set headers on WebSockets) and listen for
/// <c>notification</c>. The hub only pushes; clients never call it.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";

    public const string ClientMethod = "notification";
}

/// <summary>SignalR addresses users by the token's subject claim (the user id).</summary>
public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst(MawridClaims.Subject)?.Value;
}

/// <summary>Pushes notifications to every open connection of the recipients.</summary>
public sealed class SignalRNotificationChannel(IHubContext<NotificationsHub> hub) : INotificationChannel
{
    public string Name => "signalr";

    public Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken) =>
        hub.Clients.Users(userIds.Select(id => id.ToString()).ToList()).SendAsync(NotificationsHub.ClientMethod, notification, cancellationToken);
}

public static class NotificationsSetup
{
    public static IServiceCollection AddApiNotifications(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
        services.AddScoped<INotificationChannel, SignalRNotificationChannel>();
        return services;
    }
}
