using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Abstractions.Http;

/// <summary>
/// A feature slice's HTTP adapter for staff users, mapped under /api/v1 and requiring the
/// staff policy unless the endpoint opts out with AllowAnonymous.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>Customer-portal endpoint, mapped under /api/v1/portal with the customer policy.</summary>
public interface IPortalEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>Anonymous endpoint (branding, web forms, provider webhooks), mapped under /api/v1/public.</summary>
public interface IPublicEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>External-system endpoint authenticated by API key, mapped under /api/v1/external.</summary>
public interface IExternalEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
