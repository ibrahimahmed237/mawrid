using System.ComponentModel.DataAnnotations;

namespace Mawrid.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "mawrid";

    [Required]
    public string Audience { get; init; } = "mawrid-api";

    /// <summary>HMAC-SHA256 key. At least 32 characters. Keep it in secrets, never in a committed file for production.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
