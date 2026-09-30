using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Users.List;

internal sealed class ListUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users", async ([AsParameters] ListUsersQuery query, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(query, cancellationToken);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.UsersManage)
            .WithName("ListUsers")
            .WithTags("Users")
            .Produces<ApiResponse<IReadOnlyList<UserListItemResponse>>>();
}
