using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tests.Users;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    [Fact]
    public void IssuedTokenIsActiveUntilExpiry()
    {
        var token = Issue();

        Assert.True(token.IsActive(Now));
        Assert.False(token.IsActive(Now + Lifetime));
        Assert.False(token.IsSpent);
    }

    [Fact]
    public void RotateSpendsTokenAndKeepsSession()
    {
        var token = Issue();

        var next = token.Rotate("hash-2", Now.AddMinutes(5), Lifetime, "10.0.0.1", "agent");

        Assert.True(token.IsSpent);
        Assert.False(token.IsActive(Now.AddMinutes(5)));
        Assert.Equal(next.Id, token.ReplacedByTokenId);
        Assert.Equal(token.SessionId, next.SessionId);
        Assert.True(next.IsActive(Now.AddMinutes(5)));
    }

    [Fact]
    public void SpentTokenCannotRotateAgain()
    {
        var token = Issue();
        token.Rotate("hash-2", Now, Lifetime, null, null);

        var exception = Assert.Throws<DomainException>(() => token.Rotate("hash-3", Now, Lifetime, null, null));

        Assert.Equal(RefreshToken.InactiveCode, exception.Code);
    }

    [Fact]
    public void RevokeKeepsFirstReason()
    {
        var token = Issue();

        token.Revoke(Now, RefreshTokenRevocationReason.Logout);
        token.Revoke(Now.AddMinutes(1), RefreshTokenRevocationReason.ReuseDetected);

        Assert.True(token.IsSpent);
        Assert.Equal(RefreshTokenRevocationReason.Logout, token.RevokedReason);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void TruncatesLongUserAgent()
    {
        var token = RefreshToken.Issue(UserId.New(), "hash", Now, Lifetime, null, new string('x', 2_000));

        Assert.Equal(RefreshToken.UserAgentMaxLength, token.UserAgent!.Length);
    }

    private static RefreshToken Issue() => RefreshToken.Issue(UserId.New(), "hash-1", Now, Lifetime, "10.0.0.1", "agent");
}
