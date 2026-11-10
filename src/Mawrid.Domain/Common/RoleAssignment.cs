namespace Mawrid.Domain.Common;

/// <summary>
/// A role given to a user, optionally limited to one cost center.
/// Example: Approver for cost center IT-001 only.
/// </summary>
public sealed record RoleAssignment(Role Role, Guid? CostCenterId = null)
{
    public bool IsScoped => CostCenterId is not null;

    public override string ToString() => IsScoped ? $"{Role}@{CostCenterId}" : Role.ToString();
}
