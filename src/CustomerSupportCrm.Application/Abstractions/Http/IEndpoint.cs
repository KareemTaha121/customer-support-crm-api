using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Abstractions.Http;

/// <summary>
/// A feature slice's HTTP adapter. Implementations are discovered automatically
/// and mapped under the /api/v1 route group.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
