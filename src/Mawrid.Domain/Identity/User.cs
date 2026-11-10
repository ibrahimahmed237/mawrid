using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

/// <summary>
/// A person who uses Mawrid: an employee of the buyer, or a contact at a supplier.
/// Passwords and lockout are handled by ASP.NET Core Identity in Infrastructure;
/// this class holds only the business side of a user.
/// </summary>
public sealed class User : AggregateRoot<Guid>, ITenantOwned
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 200;

    private readonly List<RoleAssignment> _roles = [];

    private User(Guid id, Guid organizationId, string email, string displayName, Guid? supplierId) : base(id)
    {
        OrganizationId = organizationId;
        Email = email;
        DisplayName = displayName;
        SupplierId = supplierId;
        IsActive = true;
    }

    private User()
    {
        Email = null!;
        DisplayName = null!;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Stored in lower case. Unique across the whole system, because it is the login name.</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Set only for supplier users: the supplier company they work for.</summary>
    public Guid? SupplierId { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<RoleAssignment> Roles => _roles;

    public bool IsSupplierUser => SupplierId is not null;

    public static Result<User> Create(
        Guid organizationId,
        string email,
        string displayName,
        IReadOnlyCollection<RoleAssignment> roles,
        Guid? supplierId = null)
    {
        email = email?.Trim().ToLowerInvariant() ?? string.Empty;
        displayName = displayName?.Trim() ?? string.Empty;

        if (email.Length > EmailMaxLength || !email.Contains('@', StringComparison.Ordinal))
        {
            return IdentityErrors.EmailInvalid;
        }

        if (displayName.Length is < 2 or > DisplayNameMaxLength)
        {
            return IdentityErrors.DisplayNameInvalid;
        }

        var rolesCheck = ValidateRoles(roles, supplierId);
        if (rolesCheck.IsFailure)
        {
            return rolesCheck.Error!;
        }

        var user = new User(Guid.CreateVersion7(), organizationId, email, displayName, supplierId);
        user._roles.AddRange(roles.Distinct());
        user.Raise(new UserCreated(user.Id, organizationId));
        return user;
    }

    /// <summary>Replaces all roles of the user.</summary>
    public Result ReplaceRoles(IReadOnlyCollection<RoleAssignment> roles)
    {
        var rolesCheck = ValidateRoles(roles, SupplierId);
        if (rolesCheck.IsFailure)
        {
            return rolesCheck;
        }

        _roles.Clear();
        _roles.AddRange(roles.Distinct());
        Raise(new UserRolesChanged(Id, OrganizationId));
        return Result.Success();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new UserDeactivated(Id, OrganizationId));
    }

    public bool HasRole(Role role) => _roles.Any(r => r.Role == role);

    private static Result ValidateRoles(IReadOnlyCollection<RoleAssignment> roles, Guid? supplierId)
    {
        if (roles.Count == 0)
        {
            return IdentityErrors.RolesRequired;
        }

        if (roles.Any(r => r.IsScoped && !r.Role.CanBeScoped()))
        {
            return IdentityErrors.RoleCannotBeScoped;
        }

        var hasSupplierRole = roles.Any(r => r.Role == Role.Supplier);

        // A supplier user is an outsider: they get the supplier role and nothing else.
        if (hasSupplierRole && (supplierId is null || roles.Any(r => r.Role != Role.Supplier)))
        {
            return IdentityErrors.SupplierRoleRules;
        }

        if (!hasSupplierRole && supplierId is not null)
        {
            return IdentityErrors.SupplierRoleRules;
        }

        return Result.Success();
    }
}

public sealed record UserCreated(Guid UserId, Guid OrganizationId) : IDomainEvent;

public sealed record UserRolesChanged(Guid UserId, Guid OrganizationId) : IDomainEvent;

public sealed record UserDeactivated(Guid UserId, Guid OrganizationId) : IDomainEvent;
