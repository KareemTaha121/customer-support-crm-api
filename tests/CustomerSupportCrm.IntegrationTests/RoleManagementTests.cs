using System.Net;
using CustomerSupportCrm.Domain.Roles;

namespace CustomerSupportCrm.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class RoleManagementTests(PostgresApiFactory factory)
{
    private const string Password = "a-long-enough-password";

    private readonly ApiTestClient _api = new(factory.CreateApiClient());

    [Fact]
    public async Task CreateUpdateAndDeleteRole()
    {
        var admin = await _api.LoginAsAdminAsync();
        var name = UniqueRoleName();

        using var created = await _api.SendAsync(HttpMethod.Post, "/api/v1/roles", admin.AccessToken, new { name, description = "Reviews", permissions = new[] { Permissions.TicketsView } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ApiTestClient.ReadJsonAsync(created)).GetProperty("data").GetProperty("id").GetGuid();

        using (var updated = await _api.SendAsync(HttpMethod.Put, $"/api/v1/roles/{id}", admin.AccessToken, new { name, description = (string?)null, permissions = new[] { Permissions.TicketsView, Permissions.ReportsView } }))
        {
            var permissions = (await ApiTestClient.ReadJsonAsync(updated)).GetProperty("data").GetProperty("permissions");
            Assert.Equal([Permissions.ReportsView, Permissions.TicketsView], permissions.EnumerateArray().Select(p => p.GetString()));
        }

        using (var deleted = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/roles/{id}", admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        using var missing = await _api.SendAsync(HttpMethod.Get, $"/api/v1/roles/{id}", admin.AccessToken);
        Assert.Equal("ROLE_NOT_FOUND", await ApiTestClient.ErrorCodeAsync(missing));
    }

    [Fact]
    public async Task RoleNamesAreUniqueCaseInsensitively()
    {
        var admin = await _api.LoginAsAdminAsync();
        var name = UniqueRoleName();
        using (await _api.SendAsync(HttpMethod.Post, "/api/v1/roles", admin.AccessToken, new { name, permissions = Array.Empty<string>() }))
        {
        }

        using var duplicate = await _api.SendAsync(HttpMethod.Post, "/api/v1/roles", admin.AccessToken, new { name = name.ToUpperInvariant(), permissions = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("ROLE_NAME_TAKEN", await ApiTestClient.ErrorCodeAsync(duplicate));
    }

    [Fact]
    public async Task SystemRoleIsImmutable()
    {
        var admin = await _api.LoginAsAdminAsync();
        var administratorId = await _api.RoleIdAsync(admin.AccessToken, Role.AdministratorName);

        using var update = await _api.SendAsync(HttpMethod.Put, $"/api/v1/roles/{administratorId}", admin.AccessToken, new { name = "Root", permissions = Array.Empty<string>() });
        using var delete = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/roles/{administratorId}", admin.AccessToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, update.StatusCode);
        Assert.Equal("ROLE_IS_SYSTEM", await ApiTestClient.ErrorCodeAsync(update));
        Assert.Equal("ROLE_IS_SYSTEM", await ApiTestClient.ErrorCodeAsync(delete));
    }

    [Fact]
    public async Task AssignedRoleCannotBeDeleted()
    {
        var admin = await _api.LoginAsAdminAsync();
        var roleId = await CreateRoleAsync(admin.AccessToken, [Permissions.TicketsView]);
        await _api.CreateUserAsync(admin.AccessToken, ApiTestClient.UniqueEmail("holder"), Password, roleId);

        using var response = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/roles/{roleId}", admin.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ROLE_IN_USE", await ApiTestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task UnknownPermissionIsAValidationError()
    {
        var admin = await _api.LoginAsAdminAsync();

        using var response = await _api.SendAsync(HttpMethod.Post, "/api/v1/roles", admin.AccessToken, new { name = UniqueRoleName(), permissions = new[] { "tickets.teleport" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UNKNOWN_PERMISSION", await ApiTestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task PermissionChangesReachUsersOnRefresh()
    {
        var admin = await _api.LoginAsAdminAsync();
        var roleId = await CreateRoleAsync(admin.AccessToken, [Permissions.TicketsView]);
        var email = ApiTestClient.UniqueEmail("refresh-perms");
        await _api.CreateUserAsync(admin.AccessToken, email, Password, roleId);
        var session = await _api.LoginAsync(email, Password);
        Assert.Equal([Permissions.TicketsView], session.Permissions);

        var role = await _api.GetDataAsync($"/api/v1/roles/{roleId}", admin.AccessToken);
        using (await _api.SendAsync(HttpMethod.Put, $"/api/v1/roles/{roleId}", admin.AccessToken, new { name = role.GetProperty("name").GetString(), permissions = new[] { Permissions.TicketsView, Permissions.CustomersView } }))
        {
        }

        using var refreshed = await _api.RefreshRawAsync(session.RefreshToken);
        var next = await ApiTestClient.ReadSessionAsync(refreshed);

        Assert.Contains(Permissions.CustomersView, next.Permissions);
    }

    [Fact]
    public async Task PermissionCatalogIsListed()
    {
        var admin = await _api.LoginAsAdminAsync();

        var catalog = await _api.GetDataAsync("/api/v1/permissions", admin.AccessToken);

        Assert.Equal(Permissions.All.Count, catalog.GetArrayLength());
        Assert.Contains(catalog.EnumerateArray(), p => p.GetProperty("code").GetString() == Permissions.TicketsAssign && p.GetProperty("group").GetString() == "tickets");
    }

    private async Task<Guid> CreateRoleAsync(string adminToken, string[] permissions)
    {
        using var response = await _api.SendAsync(HttpMethod.Post, "/api/v1/roles", adminToken, new { name = UniqueRoleName(), permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ApiTestClient.ReadJsonAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private static string UniqueRoleName() => $"Role {Guid.NewGuid():N}"[..20];
}
