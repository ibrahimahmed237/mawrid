using System.Security.Claims;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;

namespace Mawrid.Api.Common.Security;

/// <summary>
/// Copies the caller from the validated access token into the scoped
/// <see cref="CurrentUserContext"/>, so handlers and the tenant filter can use it.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, CurrentUserContext currentUser)
    {
        currentUser.SetCorrelationId(httpContext.TraceIdentifier);

        var principal = httpContext.User;
        if (principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirstValue(MawridClaims.Subject), out var userId)
            && Guid.TryParse(principal.FindFirstValue(MawridClaims.Organization), out var organizationId))
        {
            Guid? supplierId = Guid.TryParse(principal.FindFirstValue(MawridClaims.Supplier), out var parsed) ? parsed : null;
            currentUser.SetUser(userId, organizationId, ReadRoles(principal), supplierId);
        }

        await next(httpContext);
    }

    private static List<RoleAssignment> ReadRoles(ClaimsPrincipal principal)
    {
        var roles = new List<RoleAssignment>();

        foreach (var value in principal.FindAll(MawridClaims.Role).Select(c => c.Value))
        {
            if (Enum.TryParse<Role>(value, out var role))
            {
                roles.Add(new RoleAssignment(role));
            }
        }

        // Scoped roles look like "Approver@3f2a...".
        foreach (var value in principal.FindAll(MawridClaims.RoleScope).Select(c => c.Value))
        {
            var parts = value.Split('@', 2);
            if (parts.Length == 2 && Enum.TryParse<Role>(parts[0], out var role) && Guid.TryParse(parts[1], out var costCenterId))
            {
                roles.Add(new RoleAssignment(role, costCenterId));
            }
        }

        return roles;
    }
}
