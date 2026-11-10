using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;

namespace Mawrid.Infrastructure.Identity;

/// <summary>
/// Scoped holder of the current caller. The API fills it from the access token;
/// background jobs fill it with <see cref="RunAsSystem"/> for one organization.
/// Until it is filled, <see cref="OrganizationId"/> is null and tenant queries return nothing.
/// </summary>
public sealed class CurrentUserContext : ICurrentUser
{
    private IReadOnlyList<RoleAssignment> _roles = [];

    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public Guid? SupplierId { get; private set; }

    public IReadOnlyList<RoleAssignment> Roles => _roles;

    public string? CorrelationId { get; private set; }

    /// <summary>True for background work that acts for the system, not for a person.</summary>
    public bool IsSystem { get; private set; }

    public bool HasRole(Role role) => _roles.Any(r => r.Role == role);

    public void SetUser(Guid userId, Guid organizationId, IReadOnlyList<RoleAssignment> roles, Guid? supplierId)
    {
        UserId = userId;
        OrganizationId = organizationId;
        SupplierId = supplierId;
        _roles = roles;
        IsSystem = false;
    }

    /// <summary>Acts as the system for one organization, or for no organization (null) during bootstrap.</summary>
    public void RunAsSystem(Guid? organizationId)
    {
        UserId = null;
        OrganizationId = organizationId;
        SupplierId = null;
        _roles = [];
        IsSystem = true;
    }

    public void SetCorrelationId(string correlationId) => CorrelationId = correlationId;
}
