using Mawrid.Domain.Identity;

namespace Mawrid.Application.Common.Security;

/// <summary>Creates access tokens and refresh token values.</summary>
public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user);

    /// <summary>A new random refresh token. Only <see cref="NewRefreshToken.Hash"/> is stored.</summary>
    NewRefreshToken CreateRefreshToken();

    string HashRefreshToken(string token);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record NewRefreshToken(string Token, string Hash);
