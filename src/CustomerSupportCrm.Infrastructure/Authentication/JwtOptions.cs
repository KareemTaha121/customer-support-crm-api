using System.ComponentModel.DataAnnotations;

namespace CustomerSupportCrm.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key, at least 32 characters. Supply from a secret store; never commit.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenLifetimeDays { get; init; } = 14;

    [Range(1, 24)]
    public int PortalTokenLifetimeHours { get; init; } = 8;
}
