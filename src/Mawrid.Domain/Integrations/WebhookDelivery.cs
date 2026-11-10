using Mawrid.Domain.Common;

namespace Mawrid.Domain.Integrations;

/// <summary>
/// One event for one subscription, with its delivery attempts. Failed calls are retried with a
/// growing wait; after <see cref="MaxAttempts"/> the delivery is Failed and an admin can replay it.
/// The receiver uses the delivery id to ignore a call it already processed.
/// </summary>
public sealed class WebhookDelivery : Entity<Guid>, ITenantOwned
{
    public const int MaxAttempts = 8;
    public const int ErrorMaxLength = 500;

    private WebhookDelivery(Guid id) : base(id)
    {
        EventType = null!;
        Payload = null!;
    }

    private WebhookDelivery()
    {
        EventType = null!;
        Payload = null!;
    }

    public Guid OrganizationId { get; private set; }

    public Guid SubscriptionId { get; private set; }

    public string EventType { get; private set; }

    /// <summary>The JSON body, built once so every retry sends the same bytes.</summary>
    public string Payload { get; private set; }

    public WebhookDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public static WebhookDelivery Create(Guid id, Guid organizationId, Guid subscriptionId, string eventType, string payload, DateTimeOffset now) => new(id)
    {
        OrganizationId = organizationId,
        SubscriptionId = subscriptionId,
        EventType = eventType,
        Payload = payload,
        Status = WebhookDeliveryStatus.Pending,
        CreatedAtUtc = now,
        NextAttemptAtUtc = now,
    };

    public void RecordSuccess(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAtUtc = now;
        LastStatusCode = statusCode;
        LastError = null;
        Status = WebhookDeliveryStatus.Succeeded;
        DeliveredAtUtc = now;
    }

    public void RecordFailure(int? statusCode, string error, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAtUtc = now;
        LastStatusCode = statusCode;
        LastError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = WebhookDeliveryStatus.Failed;
            return;
        }

        NextAttemptAtUtc = now + Backoff(Attempts);
    }

    /// <summary>Sends a delivery again, for example after the ERP was fixed. Allowed for any delivery.</summary>
    public void Replay(DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAtUtc = now;
    }

    /// <summary>1, 2, 4 ... minutes, at most 6 hours.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(Math.Pow(2, attempts - 1), 360));
}

public enum WebhookDeliveryStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}
