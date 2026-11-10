using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Mawrid.Infrastructure.Identity;

/// <summary>Password checks and lockout on top of ASP.NET Core Identity's <see cref="UserManager{TUser}"/>.</summary>
public sealed class IdentityService(UserManager<UserCredential> userManager) : IIdentityService
{
    public async Task<Result> CreateCredentialAsync(Guid userId, string email, string password, CancellationToken cancellationToken)
    {
        var credential = new UserCredential { Id = userId, UserName = email, Email = email, LockoutEnabled = true };
        var result = await userManager.CreateAsync(credential, password);

        return result.Succeeded
            ? Result.Success()
            : Error.Validation(IdentityErrors.PasswordRejected.Code, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task<Result<Guid>> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var credential = await userManager.FindByNameAsync(email.Trim().ToLowerInvariant());
        if (credential is null)
        {
            return IdentityErrors.InvalidCredentials;
        }

        if (await userManager.IsLockedOutAsync(credential))
        {
            return IdentityErrors.LockedOut;
        }

        if (!await userManager.CheckPasswordAsync(credential, password))
        {
            await userManager.AccessFailedAsync(credential);
            return await userManager.IsLockedOutAsync(credential) ? IdentityErrors.LockedOut : IdentityErrors.InvalidCredentials;
        }

        await userManager.ResetAccessFailedCountAsync(credential);
        return credential.Id;
    }
}
