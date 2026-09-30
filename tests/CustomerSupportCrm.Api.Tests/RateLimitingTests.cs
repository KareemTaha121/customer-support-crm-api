using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CustomerSupportCrm.Api.Tests;

public sealed class LowRateLimitApiFactory : ApiFactory
{
    public const int Permits = 2;

    protected override int RateLimitPermits => Permits;
}

public sealed class RateLimitingTests(LowRateLimitApiFactory factory) : IClassFixture<LowRateLimitApiFactory>
{
    [Fact]
    public async Task LoginIsRateLimitedWithEnvelopeAndRetryAfter()
    {
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < LowRateLimitApiFactory.Permits; attempt++)
        {
            // Invalid input fails validation, so no database is needed.
            using var allowed = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });
        var body = await limited.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("RATE_LIMITED", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.True(limited.Headers.Contains("Retry-After"));
    }
}
