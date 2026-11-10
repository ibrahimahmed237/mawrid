using System.Text.Json;
using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Events;
using Mawrid.Domain.Common;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Outbox;

/// <summary>
/// Delivers outbox messages to their MediatR handlers.
/// <list type="number">
/// <item>Claims a batch with one UPDATE (<c>READPAST</c> skips rows another worker holds).</item>
/// <item>Delivers each message in its own scope, as the system user of the message's organization.</item>
/// <item>Marks it processed, or schedules a retry with exponential backoff, or dead-letters it.</item>
/// </list>
/// Delivery is at-least-once: handlers must be idempotent.
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    /// <summary>Processes one batch. Returns how many messages were claimed.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimBatchAsync(cancellationToken);

        foreach (var message in claimed)
        {
            await DeliverAsync(message, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task<List<OutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = clock.UtcNow;
        var lockedUntil = now + LockDuration;
        var batchSize = options.Value.BatchSize;

        // One atomic statement: pick the oldest due messages, lock them, and return them.
        var claimed = await db.OutboxMessages
            .FromSql($"""
                WITH batch AS (
                    SELECT TOP ({batchSize}) *
                    FROM platform.OutboxMessages WITH (ROWLOCK, READPAST, UPDLOCK)
                    WHERE ProcessedOnUtc IS NULL
                      AND DeadLetteredOnUtc IS NULL
                      AND NextAttemptAtUtc <= {now}
                      AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {now})
                    ORDER BY OccurredOnUtc
                )
                UPDATE batch
                SET LockedUntilUtc = {lockedUntil}
                OUTPUT inserted.*
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // OUTPUT does not keep the CTE order, so sort again: events are delivered oldest first.
        return claimed.OrderBy(m => m.OccurredOnUtc).ThenBy(m => m.Id).ToList();
    }

    private async Task DeliverAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        string? error = null;

        try
        {
            await PublishAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex.Message;
            LogDeliveryFailed(ex, message.Id, message.Type, message.Attempts + 1);
        }

        await RecordOutcomeAsync(message, error, cancellationToken);
    }

    private async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = DomainEventTypes.Find(message.Type)
            ?? throw new InvalidOperationException($"Unknown domain event type '{message.Type}'.");
        var domainEvent = (IDomainEvent)JsonSerializer.Deserialize(message.Content, eventType, JsonSerializerOptions.Web)!;

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        context.RunAsSystem(message.OrganizationId);
        if (message.CorrelationId is not null)
        {
            context.SetCorrelationId(message.CorrelationId);
        }

        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(eventType);
        var notification = Activator.CreateInstance(notificationType, message.Id, domainEvent)!;
        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(notification, cancellationToken);
    }

    private async Task RecordOutcomeAsync(OutboxMessage message, string? error, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().RunAsSystem(null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.UtcNow;
        var attempts = message.Attempts + 1;

        if (error is null)
        {
            await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedOnUtc, now)
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null), cancellationToken);
            return;
        }

        var deadLetter = attempts >= options.Value.MaxAttempts;
        var nextAttempt = now + Backoff(attempts);
        var lastError = error.Length > 2000 ? error[..2000] : error;

        await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Attempts, attempts)
            .SetProperty(m => m.LastError, lastError)
            .SetProperty(m => m.NextAttemptAtUtc, nextAttempt)
            .SetProperty(m => m.DeadLetteredOnUtc, deadLetter ? now : null)
            .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null), cancellationToken);

        if (deadLetter)
        {
            LogDeadLettered(message.Id, message.Type, attempts);
        }
    }

    /// <summary>2, 4, 8, 16 ... seconds, at most 30 minutes.</summary>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempts), MaxBackoff.TotalSeconds));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempt}")]
    private partial void LogDeliveryFailed(Exception exception, Guid messageId, string type, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) was dead-lettered after {Attempts} attempts")]
    private partial void LogDeadLettered(Guid messageId, string type, int attempts);
}
