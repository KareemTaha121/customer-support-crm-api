using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;


namespace CustomerSupportCrm.Application.Features.Roles.ListPermissions;

/// <summary>
/// The static permission catalog. No command/query: there is no state or orchestration,
/// so MediatR would only add indirection.
/// </summary>
internal sealed class ListPermissionsEndpoint : IEndpoint
{
    private static readonly IReadOnlyList<PermissionResponse> Catalog =
        [.. Permissions.All.Select(code => new PermissionResponse(code, code[..code.IndexOf('.', StringComparison.Ordinal)]))];

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/permissions", () => ApiResults.Ok(Catalog))
            .RequireAuthorization(PolicyNames.RolesRead)
            .WithName("ListPermissions")
            .WithTags("Roles")
            .Produces<ApiResponse<IReadOnlyList<PermissionResponse>>>();
}
