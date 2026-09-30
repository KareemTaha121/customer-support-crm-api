using System.Net;
using System.Text.Json;

namespace CustomerSupportCrm.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationFlowTests(PostgresApiFactory factory)
{
    private const string Password = "a-long-enough-password";

    private readonly ApiTestClient _api = new(factory.CreateApiClient());

    [Fact]
    public async Task LoginSetsHardenedRefreshCookieAndReturnsProfile()
    {
        using var response = await _api.LoginRawAsync(PostgresApiFactory.AdminEmail, PostgresApiFactory.AdminPassword);
        var cookie = ApiTestClient.SetCookieHeader(response)!.ToUpperInvariant();
        var session = await ApiTestClient.ReadSessionAsync(response);

        Assert.Contains("HTTPONLY", cookie, StringComparison.Ordinal);
        Assert.Contains("SECURE", cookie, StringComparison.Ordinal);
        Assert.Contains("SAMESITE=STRICT", cookie, StringComparison.Ordinal);
        Assert.Contains("PATH=/API/V1/AUTH", cookie, StringComparison.Ordinal);
        Assert.Contains(Domain.Roles.Permissions.UsersManage, session.Permissions);

        var me = await _api.GetDataAsync("/api/v1/auth/me", session.AccessToken);
        Assert.Equal(PostgresApiFactory.AdminEmail, me.GetProperty("email").GetString());
        Assert.Contains("Administrator", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmailAreIndistinguishable()
    {
        using var wrongPassword = await _api.LoginRawAsync(PostgresApiFactory.AdminEmail, "not-the-admin-password");
        using var unknownEmail = await _api.LoginRawAsync(ApiTestClient.UniqueEmail("nobody"), "not-the-admin-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ApiTestClient.ErrorCodeAsync(wrongPassword));
        Assert.Equal("INVALID_CREDENTIALS", await ApiTestClient.ErrorCodeAsync(unknownEmail));
    }

    [Fact]
    public async Task AccountLocksAfterRepeatedFailuresUntilEnabled()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("lockout");
        var userId = await _api.CreateUserAsync(admin.AccessToken, email, Password);

        for (var attempt = 0; attempt < Domain.Users.User.MaxFailedLoginAttempts; attempt++)
        {
            using var failed = await _api.LoginRawAsync(email, "wrong-password-attempt");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using (var locked = await _api.LoginRawAsync(email, Password))
        {
            Assert.Equal("ACCOUNT_LOCKED", await ApiTestClient.ErrorCodeAsync(locked));
        }

        using (var enabled = await _api.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/enable", admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        }

        await _api.LoginAsync(email, Password);
    }

    [Fact]
    public async Task RefreshRotatesTokenAndReuseRevokesTheSession()
    {
        var session = await _api.LoginAsAdminAsync();

        using var rotated = await _api.RefreshRawAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var next = await ApiTestClient.ReadSessionAsync(rotated);
        Assert.NotEqual(session.RefreshToken, next.RefreshToken);

        // Replaying the spent token signals theft: it fails and takes the successor down with it.
        using var replay = await _api.RefreshRawAsync(session.RefreshToken);
        Assert.Equal("INVALID_REFRESH_TOKEN", await ApiTestClient.ErrorCodeAsync(replay));

        using var successor = await _api.RefreshRawAsync(next.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
    }

    [Fact]
    public async Task RefreshRequiresCsrfHeader()
    {
        var session = await _api.LoginAsAdminAsync();

        using var response = await _api.RefreshRawAsync(session.RefreshToken, withCsrfHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("CSRF_VALIDATION_FAILED", await ApiTestClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task LogoutEndsTheSessionAndClearsTheCookie()
    {
        var session = await _api.LoginAsAdminAsync();

        using var logout = await _api.LogoutRawAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", ApiTestClient.SetCookieHeader(logout), StringComparison.OrdinalIgnoreCase);

        using var refresh = await _api.RefreshRawAsync(session.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordKeepsCurrentSessionAndRevokesOthers()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("password");
        await _api.CreateUserAsync(admin.AccessToken, email, Password);
        var current = await _api.LoginAsync(email, Password);
        var other = await _api.LoginAsync(email, Password);

        using (var change = await _api.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password", current.AccessToken, new { currentPassword = Password, newPassword = "a-brand-new-password" }))
        {
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }

        using (var otherRefresh = await _api.RefreshRawAsync(other.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, otherRefresh.StatusCode);
        }

        using (var currentRefresh = await _api.RefreshRawAsync(current.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.OK, currentRefresh.StatusCode);
        }

        using var oldPassword = await _api.LoginRawAsync(email, Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await _api.LoginAsync(email, "a-brand-new-password");
    }

    [Fact]
    public async Task ChangePasswordRejectsWrongCurrentPassword()
    {
        var admin = await _api.LoginAsAdminAsync();
        var email = ApiTestClient.UniqueEmail("wrong-current");
        await _api.CreateUserAsync(admin.AccessToken, email, Password);
        var session = await _api.LoginAsync(email, Password);

        using var response = await _api.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password", session.AccessToken, new { currentPassword = "not-my-password", newPassword = "a-brand-new-password" });
        var error = (await ApiTestClient.ReadJsonAsync(response)).GetProperty("errors")[0];

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CURRENT_PASSWORD", error.GetProperty("code").GetString());
        Assert.Equal("currentPassword", error.GetProperty("field").GetString());
    }

    [Fact]
    public async Task SignInEventsAreAuditedWithCorrelationId()
    {
        var email = ApiTestClient.UniqueEmail("audited");
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login"))
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(new { email, password = "whatever-password" });
            request.Headers.Add("X-Correlation-Id", "audit-probe-1");
            using var failed = await _api.Http.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var admin = await _api.LoginAsAdminAsync();
        var entries = await _api.GetDataAsync("/api/v1/audit-logs?action=auth.login.failed&pageSize=100", admin.AccessToken);

        var entry = entries.EnumerateArray().Single(e =>
            e.GetProperty("newValues") is { ValueKind: JsonValueKind.Object } values
            && values.TryGetProperty("email", out var loggedEmail)
            && loggedEmail.GetString() == email);
        Assert.Equal("audit-probe-1", entry.GetProperty("correlationId").GetString());
        Assert.Equal("unknown_email", entry.GetProperty("newValues").GetProperty("reason").GetString());
        Assert.DoesNotContain("whatever-password", entry.GetRawText(), StringComparison.Ordinal);
    }
}
