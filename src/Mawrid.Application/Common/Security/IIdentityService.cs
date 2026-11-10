using Mawrid.Domain.Common;

namespace Mawrid.Application.Common.Security;

/// <summary>Passwords and lockout. Implemented with ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    /// <summary>Creates login credentials for an existing user. Fails if the password breaks the rules.</summary>
    Task<Result> CreateCredentialAsync(Guid userId, string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the password and counts failed attempts. Returns the user id on success,
    /// <c>auth.invalid_credentials</c> on a wrong email or password, and <c>auth.locked_out</c> after too many failures.
    /// </summary>
    Task<Result<Guid>> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken);
}
