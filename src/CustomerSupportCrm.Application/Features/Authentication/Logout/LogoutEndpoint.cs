using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Authentication.Logout;

/// <summary>Anonymous: the access token may already have expired when the user signs out.</summary>
internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/logout", async (ISender sender, HttpContext http, CancellationToken cancellationToken) =>
            {
                await sender.Send(new LogoutCommand(AuthenticationHttp.ReadRefreshCookie(http.Request)), cancellationToken);
                AuthenticationHttp.DeleteRefreshCookie(http.Response);
                return ApiResults.Success();
            })
            .AllowAnonymous()
            .RequireCsrfHeader()
            .WithName("Logout")
            .WithTags("Authentication")
            .Produces<ApiResponse<object?>>();
}
