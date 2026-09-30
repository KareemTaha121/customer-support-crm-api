using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.Create;

internal sealed class CreateUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users", async (CreateUserRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var user = await sender.Send(
                    new CreateUserCommand(request.Email, request.DisplayName, request.Password, request.RoleIds ?? [], request.Scopes),
                    cancellationToken);
                return ApiResults.Created($"/api/v1/users/{user.Id}", user);
            })
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("CreateUser")
            .WithTags("Users")
            .Produces<ApiResponse<UserResponse>>(StatusCodes.Status201Created);
}
