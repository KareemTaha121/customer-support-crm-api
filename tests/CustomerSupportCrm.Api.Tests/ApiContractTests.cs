using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CustomerSupportCrm.Api.Tests;

public sealed class ApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SuccessResponseUsesStandardEnvelope()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test/ok", UriKind.Relative));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("w-1", body.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("message").ValueKind);
        Assert.Equal(0, body.GetProperty("errors").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("meta").ValueKind);
        Assert.Equal(CorrelationIdOf(response), body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task PagedResponseIncludesPaginationMeta()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test/paged", UriKind.Relative));
        var meta = (await ReadJsonAsync(response)).GetProperty("meta");

        Assert.Equal(2, meta.GetProperty("page").GetInt32());
        Assert.Equal(1, meta.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, meta.GetProperty("totalCount").GetInt64());
        Assert.Equal(3, meta.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task CreatedResponseReturns201WithLocation()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/_test/widgets", new { name = "New", quantity = 5 });
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/v1/_test/widgets/w-42", response.Headers.Location?.OriginalString);
        Assert.Equal("New", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ValidationFailureReturnsFieldErrorsWithStableCodes()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/_test/widgets", new { name = "", quantity = 0 });
        var body = await ReadJsonAsync(response);
        var errors = body.GetProperty("errors").EnumerateArray()
            .Select(e => (Code: e.GetProperty("code").GetString(), Field: e.GetProperty("field").GetString()))
            .ToList();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal("Validation failed.", body.GetProperty("message").GetString());
        Assert.Contains(("REQUIRED", "name"), errors);
        Assert.Contains(("OUT_OF_RANGE", "quantity"), errors);
    }

    [Fact]
    public async Task MessagesAreLocalizedFromAcceptLanguage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/_test/widgets")
        {
            Content = JsonContent.Create(new { name = "", quantity = 5 }),
        };
        request.Headers.AcceptLanguage.ParseAdd("ar");

        using var response = await _client.SendAsync(request);
        var body = await ReadJsonAsync(response);
        var error = body.GetProperty("errors")[0];

        Assert.Equal("فشل التحقق من صحة البيانات.", body.GetProperty("message").GetString());
        Assert.Equal("REQUIRED", error.GetProperty("code").GetString());
        Assert.Contains("ar", response.Content.Headers.ContentLanguage);
    }

    [Fact]
    public async Task NotFoundExceptionReturnsFeatureSpecificCode()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test/not-found", UriKind.Relative));
        var body = await ReadJsonAsync(response);
        var error = body.GetProperty("errors")[0];

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("The requested resource was not found.", body.GetProperty("message").GetString());
        Assert.Equal("WIDGET_NOT_FOUND", error.GetProperty("code").GetString());
        Assert.Equal("Widget was not found.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DomainExceptionReturnsBusinessRuleViolation()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test/domain-error", UriKind.Relative));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("WIDGET_LOCKED", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnhandledExceptionReturnsGenericErrorWithoutDetails()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/_test/unhandled", UriKind.Relative));
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.DoesNotContain(TestEndpoints.SecretDetail, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.Equal(CorrelationIdOf(response), body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task UnknownRouteReturnsEnvelope()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(CorrelationIdOf(response), body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task MalformedJsonReturnsBadRequestEnvelope()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(new Uri("/api/v1/_test/widgets", UriKind.Relative), content);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("BAD_REQUEST", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApiDocumentIsServedInDevelopment()
    {
        using var response = await _client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Customer Support CRM API", body.GetProperty("info").GetProperty("title").GetString());
    }

    private static string? CorrelationIdOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(CorrelationHeader, out var values) ? values.Single() : null;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }
}
