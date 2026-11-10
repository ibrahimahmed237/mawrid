namespace Mawrid.Infrastructure.Outbox;

/// <summary>
/// A domain event saved in the same transaction as the change that raised it (ADR-0004).
/// The <see cref="OutboxProcessor"/> delivers it later, with retries.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>The event type name, used to read the JSON back.</summary>
    public required string Type { get; init; }

    public required string Content { get; init; }

    public Guid? OrganizationId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    /// <summary>A worker owns the message until this time. Stops two workers sending it twice.</summary>
    public DateTimeOffset? LockedUntilUtc { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset? ProcessedOnUtc { get; set; }

    /// <summary>Set when the message failed too many times. It is kept for inspection.</summary>
    public DateTimeOffset? DeadLetteredOnUtc { get; set; }

    public string? LastError { get; set; }
}
