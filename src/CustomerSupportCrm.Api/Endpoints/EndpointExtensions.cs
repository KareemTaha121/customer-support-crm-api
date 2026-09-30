using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Infrastructure.Realtime;

namespace CustomerSupportCrm.Api.Endpoints;

internal static class EndpointExtensions
{
    public const string ApiV1Prefix = "/api/v1";

    /// <summary>
    /// Maps every registered endpoint into its group:
    /// /api/v1 (staff), /api/v1/portal (customers), /api/v1/public (anonymous),
    /// /api/v1/external (API keys). Endpoints may opt out with AllowAnonymous.
    /// </summary>
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var staff = app.MapGroup(ApiV1Prefix).RequireAuthorization(PolicyNames.Staff);
        foreach (var endpoint in app.Services.GetServices<IEndpoint>())
        {
            endpoint.MapEndpoint(staff);
        }

        var portal = app.MapGroup($"{ApiV1Prefix}/portal").RequireAuthorization(PolicyNames.Customer).WithTags("Portal");
        foreach (var endpoint in app.Services.GetServices<IPortalEndpoint>())
        {
            endpoint.MapEndpoint(portal);
        }

        var anonymous = app.MapGroup($"{ApiV1Prefix}/public").AllowAnonymous().WithTags("Public");
        foreach (var endpoint in app.Services.GetServices<IPublicEndpoint>())
        {
            endpoint.MapEndpoint(anonymous);
        }

        var external = app.MapGroup($"{ApiV1Prefix}/external").RequireAuthorization(PolicyNames.ApiClient).WithTags("External API");
        foreach (var endpoint in app.Services.GetServices<IExternalEndpoint>())
        {
            endpoint.MapEndpoint(external);
        }

        app.MapHub<StaffHub>(StaffHub.Path);
        app.MapHub<ChatHub>(ChatHub.Path);

        return app;
    }
}
