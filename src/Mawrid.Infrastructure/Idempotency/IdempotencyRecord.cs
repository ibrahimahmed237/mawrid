namespace Mawrid.Infrastructure.Idempotency;

/// <summary>
/// Remembers one request sent with an <c>Idempotency-Key</c> header and the response we gave,
/// so a retry with the same key gets the same response instead of doing the work twice.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public required string Key { get; init; }

    /// <summary>SHA-256 of method, path and body. The same key with a different request is an error.</summary>
    public required string RequestHash { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int? StatusCode { get; set; }

    public string? ResponseContentType { get; set; }

    public string? ResponseLocation { get; set; }

    public byte[]? ResponseBody { get; set; }
}
