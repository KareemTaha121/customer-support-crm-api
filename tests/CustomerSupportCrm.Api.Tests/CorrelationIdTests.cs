namespace CustomerSupportCrm.Api.Tests;

public sealed class CorrelationIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header = "X-Correlation-Id";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task EchoesWellFormedIncomingCorrelationId()
    {
        var returned = await SendWithCorrelationIdAsync("client-req_123.abc");

        Assert.Equal("client-req_123.abc", returned);
    }

    [Fact]
    public async Task GeneratesCorrelationIdWhenMissing()
    {
        var returned = await SendWithCorrelationIdAsync(null);

        Assert.Matches("^[0-9a-f]{32}$", returned);
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("<script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ReplacesMalformedCorrelationId(string incoming)
    {
        var returned = await SendWithCorrelationIdAsync(incoming);

        Assert.NotEqual(incoming, returned);
        Assert.Matches("^[0-9a-f]{32}$", returned);
    }

    private async Task<string> SendWithCorrelationIdAsync(string? correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(Header, correlationId);
        }

        using var response = await _client.SendAsync(request);
        return response.Headers.GetValues(Header).Single();
    }
}
