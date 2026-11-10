using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;

namespace Mawrid.Application.Identity.Users;

/// <summary>A role in API requests and responses. <see cref="Role"/> is a role name such as "Approver".</summary>
public sealed record RoleAssignmentDto(string Role, Guid? CostCenterId = null)
{
    public static RoleAssignmentDto From(RoleAssignment assignment) => new(assignment.Role.ToString(), assignment.CostCenterId);

    public RoleAssignment ToDomain() => new(Enum.Parse<Role>(Role, ignoreCase: true), CostCenterId);
}

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    Guid? SupplierId,
    IReadOnlyList<RoleAssignmentDto> Roles);
