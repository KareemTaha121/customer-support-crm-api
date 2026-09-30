using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Authentication.ChangePassword;

internal sealed class ChangePasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/change-password", async (ChangePasswordRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                await sender.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken);
                return ApiResults.Success();
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("ChangePassword")
            .WithTags("Authentication")
            .Produces<ApiResponse<object?>>();
}
