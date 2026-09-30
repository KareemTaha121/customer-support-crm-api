using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CustomerSupportCrm.IntegrationTests;

public sealed record Session(string AccessToken, string RefreshToken, JsonElement User)
{
    public IReadOnlyList<string> Permissions =>
        [.. User.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
}

/// <summary>
/// Thin wrapper over the in-memory API. Cookies are handled explicitly because the refresh
/// cookie is Secure and the test server speaks plain HTTP.
/// </summary>
public sealed class ApiTestClient(HttpClient http)
{
    public const string RefreshCookie = "crm_refresh";
    public const string CsrfHeader = "X-CSRF-Protection";

    public HttpClient Http => http;

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@it.test";

    public Task<HttpResponseMessage> LoginRawAsync(string email, string password) =>
        http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    public async Task<Session> LoginAsync(string email, string password)
    {
        using var response = await LoginRawAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    public Task<Session> LoginAsAdminAsync() => LoginAsync(PostgresApiFactory.AdminEmail, PostgresApiFactory.AdminPassword);

    public async Task<HttpResponseMessage> RefreshRawAsync(string refreshToken, bool withCsrfHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");
        if (withCsrfHeader)
        {
            request.Headers.Add(CsrfHeader, "1");
        }

        return await http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> LogoutRawAsync(string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");
        request.Headers.Add(CsrfHeader, "1");
        return await http.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId);
        }

        return await http.SendAsync(request);
    }

    public async Task<JsonElement> GetDataAsync(string path, string accessToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("data");
    }

    public async Task<Guid> RoleIdAsync(string accessToken, string roleName)
    {
        var roles = await GetDataAsync("/api/v1/roles", accessToken);
        return roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == roleName).GetProperty("id").GetGuid();
    }

    /// <summary>Creates a user through the API as the administrator and returns its id.</summary>
    public async Task<Guid> CreateUserAsync(string adminToken, string email, string password, params Guid[] roleIds)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/v1/users", adminToken, new { email, displayName = "Integration User", password, roleIds });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("data").GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errors")[0].GetProperty("code").GetString();

    public static string? SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(RefreshCookie + "=", StringComparison.Ordinal))
            : null;

    public static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        var cookie = SetCookieHeader(response) ?? throw new InvalidOperationException("No refresh cookie was set.");
        var refreshToken = cookie[(RefreshCookie.Length + 1)..cookie.IndexOf(';', StringComparison.Ordinal)];

        var data = (await ReadJsonAsync(response)).GetProperty("data");
        return new Session(data.GetProperty("accessToken").GetString()!, refreshToken, data.GetProperty("user"));
    }
}
