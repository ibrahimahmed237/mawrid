using Microsoft.AspNetCore.Identity;

namespace Mawrid.Infrastructure.Identity;

/// <summary>
/// Login data managed by ASP.NET Core Identity: password hash, lockout, security stamp.
/// It has the same id as the business <see cref="Domain.Identity.User"/>.
/// </summary>
public sealed class UserCredential : IdentityUser<Guid>;
