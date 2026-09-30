using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.Api.Tests;

/// <summary>Authentication, authorization, CSRF and CORS behavior that needs no database.</summary>
public sealed class SecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ProtectedEndpointWithoutTokenReturns401Envelope()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test-secure/authenticated", UriKind.Relative));

        Assert.Equal("UNAUTHORIZED", await AssertErrorAsync(response, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task TokenWithBadSignatureIsRejected()
    {
        var token = IssueToken([]);
        var tampered = token[..^4] + (token.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        using var response = await SendAsync("/api/v1/_test-secure/authenticated", tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidTokenReachesAuthenticatedEndpoint()
    {
        using var response = await SendAsync("/api/v1/_test-secure/authenticated", IssueToken([]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingPermissionReturns403Envelope()
    {
        using var response = await SendAsync("/api/v1/_test-secure/users-manage", IssueToken([Permissions.TicketsView]));

        Assert.Equal("FORBIDDEN", await AssertErrorAsync(response, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task GrantedPermissionIsAuthorized()
    {
        using var response = await SendAsync("/api/v1/_test-secure/users-manage", IssueToken([Permissions.UsersManage]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FeatureEndpointsRequireAuthenticationByDefault()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshWithoutCsrfHeaderIsForbidden()
    {
        using var response = await _client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal("CSRF_VALIDATION_FAILED", await AssertErrorAsync(response, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task LoginValidatesInputBeforeTouchingTheDatabase()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });

        Assert.Equal("REQUIRED", await AssertErrorAsync(response, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task CorsAllowsConfiguredOriginWithCredentials()
    {
        using var response = await PreflightAsync("http://localhost:4200");

        Assert.Equal("http://localhost:4200", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
    }

    [Fact]
    public async Task CorsIgnoresUnknownOrigin()
    {
        using var response = await PreflightAsync("https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OpenApiDeclaresBearerScheme()
    {
        using var response = await _client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    private string IssueToken(string[] permissions)
    {
        var tokens = factory.Services.GetRequiredService<ITokenService>();
        var user = User.Create(EmailAddress.Create("tester@example.com"), "Tester", "unused-hash", []);
        return tokens.CreateAccessToken(user, Guid.NewGuid(), [], permissions).Token;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return await _client.SendAsync(request);
    }

    /// <summary>Asserts the error envelope and returns the first error code.</summary>
    private static async Task<string?> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("correlationId").GetString()));
        return body.GetProperty("errors")[0].GetProperty("code").GetString();
    }
}
