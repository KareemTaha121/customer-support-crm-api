using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Authentication.Login;

internal sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/login", async (LoginRequest request, ISender sender, HttpContext http, CancellationToken cancellationToken) =>
            {
                var session = await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
                AuthenticationHttp.AppendRefreshCookie(http.Response, session);
                return ApiResults.Ok(session.Response);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("Login")
            .WithTags("Authentication")
            .Produces<ApiResponse<AccessTokenResponse>>();
}
