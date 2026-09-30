using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.Enable;

internal sealed class EnableUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/{id:guid}/enable", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new EnableUserCommand(id), cancellationToken)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("EnableUser")
            .WithTags("Users")
            .Produces<ApiResponse<UserResponse>>();
}
