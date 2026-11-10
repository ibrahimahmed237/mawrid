// Roles are shared by every module for authorization, so they live in the shared kernel.
namespace Mawrid.Domain.Common;

/// <summary>What a user is allowed to do. See the personas in DESIGN section 3.</summary>
public enum Role
{
    Admin = 1,
    Requester = 2,
    Approver = 3,
    Buyer = 4,
    Receiver = 5,
    ApClerk = 6,

    /// <summary>A contact at a supplier company. Supplier users can only use the supplier portal.</summary>
    Supplier = 7,
}

public static class RoleRules
{
    /// <summary>Only these roles can be limited to one cost center.</summary>
    public static bool CanBeScoped(this Role role) => role is Role.Approver or Role.Requester;
}
