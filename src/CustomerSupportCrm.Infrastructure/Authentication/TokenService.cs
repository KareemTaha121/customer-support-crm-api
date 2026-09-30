using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportCrm.Infrastructure.Authentication;

internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    public AccessToken CreateAccessToken(User user, Guid sessionId, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);

        List<Claim> claims =
        [
            new(CrmClaimTypes.Actor, ActorTypes.Staff),
            new(CrmClaimTypes.Subject, user.Id.Value.ToString()),
            new(CrmClaimTypes.Email, user.Email),
            new(CrmClaimTypes.Name, user.DisplayName),
            new(CrmClaimTypes.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            .. roles.Select(role => new Claim(CrmClaimTypes.Role, role)),
            .. permissions.Select(permission => new Claim(CrmClaimTypes.Permission, permission)),
        ];

        return CreateToken(claims, TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes));
    }

    /// <summary>Portal token: no permissions, no refresh; the customer signs in again after it expires.</summary>
    public AccessToken CreateCustomerAccessToken(Guid accountId, Guid customerId, string email, string name) =>
        CreateToken(
            [
                new(CrmClaimTypes.Actor, ActorTypes.Customer),
                new(CrmClaimTypes.Subject, accountId.ToString()),
                new(CrmClaimTypes.CustomerId, customerId.ToString()),
                new(CrmClaimTypes.Email, email),
                new(CrmClaimTypes.Name, name),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            TimeSpan.FromHours(_options.PortalTokenLifetimeHours));

    private AccessToken CreateToken(List<Claim> claims, TimeSpan lifetime)
    {
        var now = time.GetUtcNow();
        var expiresAt = now + lifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(CreateSigningKey(_options), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }

    /// <summary>256 random bits, base64url-encoded. Only the SHA-256 hash is stored.</summary>
    public GeneratedRefreshToken GenerateRefreshToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return new GeneratedRefreshToken(token, HashRefreshToken(token));
    }

    /// <summary>A fast hash is sufficient: the input is high-entropy, not a user-chosen secret.</summary>
    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
