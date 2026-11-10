using System.Collections.Concurrent;
using Mawrid.Infrastructure.Notifications;

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
