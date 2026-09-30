using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.Roles.Create;

internal sealed class CreateRoleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/roles", async (CreateRoleRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var role = await sender.Send(
                    new CreateRoleCommand(request.Name, request.Description, request.Permissions ?? []),
                    cancellationToken);
                return ApiResults.Created($"/api/v1/roles/{role.Id}", role);
            })
            .RequireAuthorization(Permissions.RolesManage)
            .WithName("CreateRole")
            .WithTags("Roles")
            .Produces<ApiResponse<RoleResponse>>(StatusCodes.Status201Created);
}
