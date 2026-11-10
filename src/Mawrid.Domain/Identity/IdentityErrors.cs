using Mawrid.Domain.Common;

namespace Mawrid.Domain.Identity;

public static class IdentityErrors
{
    public static readonly Error OrganizationNameInvalid =
        Error.Validation("organization.name_invalid", "The organization name must be 2 to 200 characters.");

    public static readonly Error CostCenterCodeInvalid =
        Error.Validation("cost_center.code_invalid", "The code must be 2 to 20 characters: letters, digits and '-'.");

    public static readonly Error CostCenterNameInvalid =
        Error.Validation("cost_center.name_invalid", "The cost center name must be 2 to 200 characters.");

    public static readonly Error CostCenterCodeTaken =
        Error.Conflict("cost_center.code_taken", "A cost center with this code already exists.");

    public static readonly Error CostCenterNotFound =
        Error.NotFound("cost_center.not_found", "The cost center was not found.");

    public static readonly Error EmailInvalid =
        Error.Validation("user.email_invalid", "The email address is not valid.");

    public static readonly Error EmailTaken =
        Error.Conflict("user.email_taken", "A user with this email already exists.");

    public static readonly Error DisplayNameInvalid =
        Error.Validation("user.display_name_invalid", "The display name must be 2 to 200 characters.");

    public static readonly Error RolesRequired =
        Error.Validation("user.roles_required", "A user needs at least one role.");

    public static readonly Error RoleCannotBeScoped =
        Error.Validation("user.role_cannot_be_scoped", "Only Approver and Requester roles can be limited to a cost center.");

    public static readonly Error SupplierRoleRules =
        Error.Validation("user.supplier_role_rules", "Supplier users must have a supplier and only the Supplier role.");

    public static readonly Error UserNotFound =
        Error.NotFound("user.not_found", "The user was not found.");

    public static readonly Error PasswordRejected =
        Error.Validation("user.password_rejected", "The password does not meet the password rules.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The email or password is wrong.");

    public static readonly Error LockedOut =
        Error.Unauthorized("auth.locked_out", "Too many failed attempts. Try again later.");

    public static readonly Error RefreshTokenInvalid =
        Error.Unauthorized("auth.refresh_token_invalid", "The refresh token is not valid. Log in again.");

    public static readonly Error RefreshTokenReused =
        Error.Unauthorized("auth.refresh_token_reused", "This refresh token was already used. All sessions from this login were ended.");
}
