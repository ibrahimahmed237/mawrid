namespace Mawrid.Infrastructure.Identity;

/// <summary>Claim names in Mawrid access tokens.</summary>
public static class MawridClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Organization = "org";
    public const string Supplier = "supplier";

    /// <summary>One claim per role, such as <c>Buyer</c>. Used by <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Role = "role";

    /// <summary>One claim per scoped role, such as <c>Approver@{costCenterId}</c>.</summary>
    public const string RoleScope = "role_scope";
}
