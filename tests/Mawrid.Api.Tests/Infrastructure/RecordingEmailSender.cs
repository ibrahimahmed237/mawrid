using System.Collections.Concurrent;
using Mawrid.Infrastructure.Notifications;
using Mawrid.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Api.Tests.Infrastructure;

/// <summary>Keeps every email the app "sends", so tests can check who was told what.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent.ToArray();

    public IEnumerable<SentEmail> To(string address) => Sent.Where(e => e.To.Equals(address, StringComparison.OrdinalIgnoreCase));

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}

public sealed record SentEmail(string To, string Subject, string Body);

public static class OutboxTestExtensions
{
    /// <summary>Delivers every outbox message that is due, like the background worker would.</summary>
    public static async Task DeliverOutboxAsync(this MawridApiFactory factory)
    {
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0)
        {
        }
    }
}

public static class SystemCommandExtensions
{
    /// <summary>Sends a command the way a background job does: as the system user of one organization.</summary>
    public static async Task<Mawrid.Domain.Common.Result<T>> RunAsSystemAsync<T>(
        this MawridApiFactory factory, Guid organizationId, MediatR.IRequest<Mawrid.Domain.Common.Result<T>> command)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Mawrid.Infrastructure.Identity.CurrentUserContext>().RunAsSystem(organizationId);
        return await scope.ServiceProvider.GetRequiredService<MediatR.ISender>().Send(command);
    }
}
