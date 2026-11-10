using Mawrid.Domain.Common;
using FluentValidation;
using Mawrid.Domain.Identity;

namespace Mawrid.Application.Identity.Users;

internal sealed class RoleAssignmentDtoValidator : AbstractValidator<RoleAssignmentDto>
{
    public RoleAssignmentDtoValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(role => Enum.TryParse<Role>(role, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            .WithMessage(x => $"'{x.Role}' is not a known role. Use one of: {string.Join(", ", Enum.GetNames<Role>())}.");
    }
}

internal static class UserRules
{
    /// <summary>Checks that every scoped role points at a cost center of the current organization.</summary>
    public static async Task<bool> CostCentersExistAsync(
        IQueryable<CostCenter> costCenters, IEnumerable<RoleAssignment> roles, CancellationToken cancellationToken)
    {
        var ids = roles.Where(r => r.CostCenterId is not null).Select(r => r.CostCenterId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return true;
        }

        var found = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            costCenters.Where(c => ids.Contains(c.Id) && c.IsActive), cancellationToken);
        return found == ids.Count;
    }
}
