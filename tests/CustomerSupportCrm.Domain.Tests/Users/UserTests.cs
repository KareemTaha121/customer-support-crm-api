using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNormalizesEmailAndStartsActive()
    {
        var user = NewUser(email: "  Agent@Example.COM ");

        Assert.Equal("agent@example.com", user.Email);
        Assert.True(user.IsActive);
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsBlankDisplayName(string displayName)
    {
        var exception = Assert.Throws<DomainException>(() => NewUser(displayName: displayName));

        Assert.Equal(User.InvalidDisplayNameCode, exception.Code);
    }

    [Fact]
    public void LocksOutAfterMaxFailedAttempts()
    {
        var user = NewUser();

        for (var attempt = 1; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
            Assert.False(user.IsLockedOut(Now));
        }

        user.RecordFailedLogin(Now);

        Assert.True(user.IsLockedOut(Now));
        Assert.False(user.IsLockedOut(Now + User.LockoutDuration));
    }

    [Fact]
    public void SuccessfulLoginResetsFailuresAndRecordsTime()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin(Now);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Equal(Now, user.LastLoginAt);
    }

    [Fact]
    public void EnableClearsLockout()
    {
        var user = NewUser();
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        user.Disable();
        user.Enable();

        Assert.True(user.IsActive);
        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void SetRolesReplacesMembershipWithoutDuplicates()
    {
        var agent = RoleId.New();
        var manager = RoleId.New();
        var user = NewUser(roles: [agent]);

        user.SetRoles([manager, manager]);

        Assert.Equal([manager], user.RoleIds);
        Assert.False(user.HasRole(agent));
    }

    private static User NewUser(string email = "agent@example.com", string displayName = "Agent Smith", RoleId[]? roles = null) =>
        User.Create(EmailAddress.Create(email), displayName, "hash", roles ?? []);
}
