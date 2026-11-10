using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mawrid.Infrastructure.Identity;

/// <summary>Creates signed JWT access tokens and random refresh tokens.</summary>
/// <remarks>Token times always use the real system time (<see cref="TimeProvider"/>), never a business clock.</remarks>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(MawridClaims.Subject, user.Id.ToString()),
            new(MawridClaims.Email, user.Email),
            new(MawridClaims.Name, user.DisplayName),
            new(MawridClaims.Organization, user.OrganizationId.ToString()),
        };

        if (user.SupplierId is { } supplierId)
        {
            claims.Add(new Claim(MawridClaims.Supplier, supplierId.ToString()));
        }

        foreach (var role in user.Roles.Select(r => r.Role).Distinct())
        {
            claims.Add(new Claim(MawridClaims.Role, role.ToString()));
        }

        foreach (var scoped in user.Roles.Where(r => r.IsScoped))
        {
            claims.Add(new Claim(MawridClaims.RoleScope, scoped.ToString()));
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return new NewRefreshToken(token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey SigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
