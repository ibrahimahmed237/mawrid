namespace Mawrid.Application.Identity.Auth;

/// <summary>What the client gets after login or refresh.</summary>
public sealed record TokenPairDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc)
{
    public string TokenType => "Bearer";
}
