namespace Mawrid.Infrastructure.Auditing;

/// <summary>
/// One change to one record: who, when, and which values changed. Rows are only
/// ever inserted, never updated or deleted.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; init; }

    public Guid? OrganizationId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    /// <summary>Added, Modified or Deleted.</summary>
    public required string Action { get; init; }

    /// <summary>JSON: for each changed property, the old and new value.</summary>
    public required string Changes { get; init; }

    public Guid? UserId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; }
}
