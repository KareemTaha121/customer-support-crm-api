using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.GetById;

internal sealed class GetUserByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new GetUserByIdQuery(id), cancellationToken)))
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("GetUserById")
            .WithTags("Users")
            .Produces<ApiResponse<UserResponse>>();
}
