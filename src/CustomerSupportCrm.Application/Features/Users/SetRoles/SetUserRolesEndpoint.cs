using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.SetRoles;

internal sealed class SetUserRolesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/users/{id:guid}/roles", async (Guid id, SetUserRolesRequest request, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new SetUserRolesCommand(id, request.RoleIds ?? []), cancellationToken)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("SetUserRoles")
            .WithTags("Users")
            .Produces<ApiResponse<UserResponse>>();
}
