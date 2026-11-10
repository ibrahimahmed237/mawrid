namespace Mawrid.Infrastructure.Persistence;

/// <summary>
/// Shadow properties added to every aggregate root table. They live only in the
/// database model, not in domain classes. Read them with <c>EF.Property&lt;T&gt;(entity, name)</c>.
/// </summary>
public static class AuditFields
{
    public const string CreatedAtUtc = nameof(CreatedAtUtc);
    public const string CreatedBy = nameof(CreatedBy);
    public const string UpdatedAtUtc = nameof(UpdatedAtUtc);
    public const string UpdatedBy = nameof(UpdatedBy);
}
