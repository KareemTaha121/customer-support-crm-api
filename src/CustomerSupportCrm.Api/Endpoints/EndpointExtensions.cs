using CustomerSupportCrm.Application.Abstractions.Http;

namespace CustomerSupportCrm.Api.Endpoints;

internal static class EndpointExtensions
{
    public const string ApiV1Prefix = "/api/v1";

    /// <summary>
    /// Maps every registered <see cref="IEndpoint"/> under /api/v1. Endpoints require an
    /// authenticated user unless they opt out with AllowAnonymous.
    /// </summary>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup(ApiV1Prefix).RequireAuthorization();

        foreach (var endpoint in app.Services.GetServices<IEndpoint>())
        {
            endpoint.MapEndpoint(v1);
        }

        return app;
    }
}
