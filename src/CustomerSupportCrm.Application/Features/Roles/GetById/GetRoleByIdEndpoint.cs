using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Roles.GetById;

internal sealed class GetRoleByIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/roles/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(new GetRoleByIdQuery(id), cancellationToken)))
            .RequireAuthorization(PolicyNames.RolesRead)
            .WithName("GetRoleById")
            .WithTags("Roles")
            .Produces<ApiResponse<RoleResponse>>();
}
