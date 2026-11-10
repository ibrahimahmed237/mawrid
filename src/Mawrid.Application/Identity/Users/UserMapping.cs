using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;

namespace Mawrid.Application.Identity.Users;

internal static class UserMapping
{
    public static UserDto ToDto(User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.IsActive,
        user.SupplierId,
        user.Roles.Select(RoleAssignmentDto.From).ToList());
}
