using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Roles.Update;

internal sealed class UpdateRoleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/roles/{id:guid}", async (Guid id, UpdateRoleRequest request, ISender sender, CancellationToken cancellationToken) =>
                ApiResults.Ok(await sender.Send(
                    new UpdateRoleCommand(id, request.Name, request.Description, request.Permissions ?? []),
                    cancellationToken)))
            .RequireAuthorization(Permissions.RolesManage)
            .WithName("UpdateRole")
            .WithTags("Roles")
            .Produces<ApiResponse<RoleResponse>>();
}
