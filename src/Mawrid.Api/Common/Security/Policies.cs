using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Mawrid.Api.Common.Security;

/// <summary>Authorization policies. Every endpoint needs a logged-in user unless it says <c>[AllowAnonymous]</c>.</summary>
public static class Policies
{
    public const string Admin = nameof(Admin);

    /// <summary>Any employee of the buyer organization (not a supplier user).</summary>
    public const string InternalUser = nameof(InternalUser);

    public const string SupplierUser = nameof(SupplierUser);

    /// <summary>Procurement staff: Buyer or Admin.</summary>
    public const string Procurement = nameof(Procurement);

    public static void Configure(AuthorizationOptions options)
    {
        // Secure by default: an endpoint without [Authorize] still needs a valid token.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        options.AddPolicy(Admin, policy => policy.RequireRole(nameof(Role.Admin)));
        options.AddPolicy(InternalUser, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim(MawridClaims.Role, nameof(Role.Supplier))));
        options.AddPolicy(SupplierUser, policy => policy
            .RequireRole(nameof(Role.Supplier))
            .RequireClaim(MawridClaims.Supplier));
        options.AddPolicy(Procurement, policy => policy.RequireRole(nameof(Role.Buyer), nameof(Role.Admin)));
    }
}
