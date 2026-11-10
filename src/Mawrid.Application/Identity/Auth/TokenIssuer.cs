using Mawrid.Application.Common.Abstractions;
using Mawrid.Application.Common.Persistence;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Identity;

namespace Mawrid.Application.Identity.Auth;

/// <summary>Creates an access token and stores a new refresh token: a new family at login, or the next token of a family at refresh.</summary>
internal static class TokenIssuer
{
    public static async Task<TokenPairDto> IssueAsync(
        User user,
        RefreshToken? family,
        ITokenService tokens,
        IAppDbContext db,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var refresh = tokens.CreateRefreshToken();

        var stored = family is null
            ? RefreshToken.StartFamily(user.Id, refresh.Hash, now, tokens.RefreshTokenLifetime)
            : family.Rotate(refresh.Hash, now, tokens.RefreshTokenLifetime);

        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokens.CreateAccessToken(user);
        return new TokenPairDto(access.Token, access.ExpiresAtUtc, refresh.Token, stored.ExpiresAtUtc);
    }
}
