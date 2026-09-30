using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.Disable;

internal sealed class DisableUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/{id:guid}/disable", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new DisableUserCommand(id), cancellationToken)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("DisableUser")
            .WithTags("Users")
            .Produces<ApiResponse<UserResponse>>();
}
