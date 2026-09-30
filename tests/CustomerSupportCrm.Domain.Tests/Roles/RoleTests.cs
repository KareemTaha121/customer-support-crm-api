using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;

namespace CustomerSupportCrm.Domain.Tests.Roles;

public sealed class RoleTests
{
    [Fact]
    public void CreateTrimsNameAndNormalizes()
    {
        var role = Role.Create("  Support Lead ", null, [Permissions.TicketsView]);

        Assert.Equal("Support Lead", role.Name);
        Assert.Equal("SUPPORT LEAD", role.NormalizedName);
        Assert.False(role.IsSystem);
    }

    [Fact]
    public void RejectsUnknownPermission()
    {
        var exception = Assert.Throws<DomainException>(() => Role.Create("Agent", null, ["tickets.fly"]));

        Assert.Equal(Role.UnknownPermissionCode, exception.Code);
    }

    [Fact]
    public void UpdateReplacesPermissions()
    {
        var role = Role.Create("Agent", null, [Permissions.TicketsView, Permissions.TicketsCreate]);

        role.Update("Agent", "Front line", [Permissions.TicketsView, Permissions.CustomersView, Permissions.CustomersView]);

        Assert.Equal([Permissions.CustomersView, Permissions.TicketsView], role.PermissionCodes);
        Assert.Equal("Front line", role.Description);
    }

    [Fact]
    public void AdministratorHoldsEveryPermission()
    {
        var administrator = Role.CreateAdministrator();

        Assert.True(administrator.IsSystem);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), administrator.PermissionCodes);
    }

    [Fact]
    public void SystemRoleCannotBeUpdatedOrDeleted()
    {
        var administrator = Role.CreateAdministrator();

        Assert.Equal(Role.SystemRoleCode, Assert.Throws<DomainException>(() => administrator.Update("Admin", null, [])).Code);
        Assert.Equal(Role.SystemRoleCode, Assert.Throws<DomainException>(administrator.EnsureCanBeDeleted).Code);
    }

    [Fact]
    public void PermissionCatalogCodesAreUniqueAndGrouped()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Permissions.All, code => Assert.Matches("^[a-z]+\\.[a-z_]+$", code));
    }
}
