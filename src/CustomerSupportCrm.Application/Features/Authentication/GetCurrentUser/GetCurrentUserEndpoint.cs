using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Authentication.GetCurrentUser;

internal sealed class GetCurrentUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet($"{AuthenticationHttp.RoutePrefix}/me", async (ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new GetCurrentUserQuery(), cancellationToken)))
            .WithName("GetCurrentUser")
            .WithTags("Authentication")
            .Produces<ApiResponse<CurrentUserResponse>>();
}
