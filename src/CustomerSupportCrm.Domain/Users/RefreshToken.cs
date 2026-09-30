using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Users;

public enum RefreshTokenRevocationReason
{
    Logout,
    ReuseDetected,
    UserDisabled,
    PasswordChanged,
}

/// <summary>
/// One link in a refresh-token rotation chain. Only a hash of the token is stored.
/// All tokens issued from one sign-in share a <see cref="SessionId"/>, so presenting an
/// already-used token can revoke the whole session.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    public const int UserAgentMaxLength = 512;
    public const int IpAddressMaxLength = 64;

    public const string InactiveCode = "REFRESH_TOKEN_INACTIVE";

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    private RefreshToken(Guid id, UserId userId, Guid sessionId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? ipAddress, string? userAgent)
        : base(id)
    {
        UserId = userId;
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = Truncate(ipAddress, IpAddressMaxLength);
        UserAgent = Truncate(userAgent, UserAgentMaxLength);
    }

    public UserId UserId { get; private set; }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevocationReason? RevokedReason { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Starts a new session.</summary>
    public static RefreshToken Issue(UserId userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string? ipAddress, string? userAgent) =>
        Create(userId, Guid.CreateVersion7(), tokenHash, now, lifetime, ipAddress, userAgent);

    public bool IsActive(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>True when this token was already rotated or revoked; presenting it again indicates theft.</summary>
    public bool IsSpent => UsedAt is not null || RevokedAt is not null;

    /// <summary>Consumes this token and returns its successor in the same session.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime, string? ipAddress, string? userAgent)
    {
        if (!IsActive(now))
        {
            throw new DomainException(InactiveCode, "The refresh token is no longer active.");
        }

        var next = Create(UserId, SessionId, newTokenHash, now, lifetime, ipAddress, userAgent);
        UsedAt = now;
        ReplacedByTokenId = next.Id;
        return next;
    }

    public void Revoke(DateTimeOffset now, RefreshTokenRevocationReason reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    private static RefreshToken Create(UserId userId, Guid sessionId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string? ipAddress, string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        return new RefreshToken(Guid.CreateVersion7(), userId, sessionId, tokenHash, now, now + lifetime, ipAddress, userAgent);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } ? value[..Math.Min(value.Length, maxLength)] : null;
}
