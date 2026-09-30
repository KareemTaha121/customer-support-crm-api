using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Roles.List;

internal sealed class ListRolesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/roles", async (ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new ListRolesQuery(), cancellationToken)))
            .RequireAuthorization(PolicyNames.RolesRead)
            .WithName("ListRoles")
            .WithTags("Roles")
            .Produces<ApiResponse<IReadOnlyList<RoleResponse>>>();
}
