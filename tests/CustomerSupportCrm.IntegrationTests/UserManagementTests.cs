using System.Net;

namespace CustomerSupportCrm.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class UserManagementTests(PostgresApiFactory factory)
{
    private const string Password = "a-long-enough-password";

    private readonly ApiTestClient _api = new(factory.CreateApiClient());

    [Fact]
    public async Task CreateThenListAndGetUser()
    {
        var admin = await _api.LoginAsAdminAsync();
        var agentRole = await _api.RoleIdAsync(admin.AccessToken, "Agent");
        var email = ApiTestClient.UniqueEmail("Created");

        using var created = await _api.SendAsync(HttpMethod.Post, "/api/v1/users", admin.AccessToken, new { email, displayName = "Created User", password = Password, roleIds = new[] { agentRole } });
        var user = (await ApiTestClient.ReadJsonAsync(created)).GetProperty("data");
        var id = user.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/users/{id}", created.Headers.Location?.OriginalString);
        Assert.Equal(email, user.GetProperty("email").GetString(), ignoreCase: true);
        Assert.Equal("Agent", user.GetProperty("roles")[0].GetProperty("name").GetString());

        var list = await _api.GetDataAsync($"/api/v1/users?search={Uri.EscapeDataString(email[..20])}", admin.AccessToken);
        Assert.Contains(list.EnumerateArray(), u => u.GetProperty("id").GetGuid() == id);

        var fetched = await _api.GetDataAsync($"/api/v1/users/{id}", admin.AccessToken);
        Assert.Equal("Active", fetched.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DuplicateEmailIsRejectedCaseInsensitively()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("dupe");
        await _api.CreateUserAsync(admin.AccessToken, email, Password);

        using var duplicate = await _api.SendAsync(HttpMethod.Post, "/api/v1/users", admin.AccessToken, new { email = email.ToUpperInvariant(), displayName = "Dupe", password = Password, roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("EMAIL_TAKEN", await ApiTestClient.ErrorCodeAsync(duplicate));
    }

    [Fact]
    public async Task UnknownRoleIsAValidationError()
    {
        var admin = await _api.LoginAsAdminAsync();

        using var response = await _api.SendAsync(HttpMethod.Post, "/api/v1/users", admin.AccessToken, new { email = ApiTestClient.UniqueEmail("norole"), displayName = "No Role", password = Password, roleIds = new[] { Guid.NewGuid() } });
        var error = (await ApiTestClient.ReadJsonAsync(response)).GetProperty("errors")[0];

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UNKNOWN_ROLE", error.GetProperty("code").GetString());
        Assert.Equal("roleIds", error.GetProperty("field").GetString());
    }

    [Fact]
    public async Task AgentCannotManageUsersOrReadRoles()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("agent");
        await _api.CreateUserAsync(admin.AccessToken, email, Password, await _api.RoleIdAsync(admin.AccessToken, "Agent"));
        var agent = await _api.LoginAsync(email, Password);

        using var users = await _api.SendAsync(HttpMethod.Get, "/api/v1/users", agent.AccessToken);
        using var roles = await _api.SendAsync(HttpMethod.Get, "/api/v1/roles", agent.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);
        Assert.DoesNotContain(Domain.Roles.Permissions.UsersManage, agent.Permissions);
    }

    [Fact]
    public async Task DisablingAUserBlocksSignInAndRefresh()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("disabled");
        var userId = await _api.CreateUserAsync(admin.AccessToken, email, Password);
        var session = await _api.LoginAsync(email, Password);

        using (var disabled = await _api.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/disable", admin.AccessToken))
        {
            Assert.Equal("Disabled", (await ApiTestClient.ReadJsonAsync(disabled)).GetProperty("data").GetProperty("status").GetString());
        }

        using (var refresh = await _api.RefreshRawAsync(session.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        using var login = await _api.LoginRawAsync(email, Password);
        Assert.Equal("ACCOUNT_DISABLED", await ApiTestClient.ErrorCodeAsync(login));
    }

    [Fact]
    public async Task AdministratorCannotDisableThemselves()
    {
        var admin = await _api.LoginAsAdminAsync();
        var adminId = admin.User.GetProperty("id").GetGuid();

        using var response = await _api.SendAsync(HttpMethod.Post, $"/api/v1/users/{adminId}/disable", admin.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CANNOT_DISABLE_SELF", await ApiTestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task LastActiveAdministratorKeepsTheRole()
    {
        var admin = await _api.LoginAsAdminAsync();
        var adminId = admin.User.GetProperty("id").GetGuid();

        using var response = await _api.SendAsync(HttpMethod.Put, $"/api/v1/users/{adminId}/roles", admin.AccessToken, new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LAST_ADMINISTRATOR", await ApiTestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task UnknownUserReturnsNotFound()
    {
        var admin = await _api.LoginAsAdminAsync();

        using var response = await _api.SendAsync(HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}", admin.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("USER_NOT_FOUND", await ApiTestClient.ErrorCodeAsync(response));
    }
}
