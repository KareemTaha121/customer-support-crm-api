using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Authentication.Refresh;

internal sealed class RefreshSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/refresh", async (ISender sender, HttpContext http, CancellationToken cancellationToken) =>
            {
                var session = await sender.Send(new RefreshSessionCommand(AuthenticationHttp.ReadRefreshCookie(http.Request)), cancellationToken);
                AuthenticationHttp.AppendRefreshCookie(http.Response, session);
                return ApiResults.Ok(session.Response);
            })
            .AllowAnonymous()
            .RequireCsrfHeader()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("RefreshSession")
            .WithTags("Authentication")
            .Produces<ApiResponse<AccessTokenResponse>>();
}
