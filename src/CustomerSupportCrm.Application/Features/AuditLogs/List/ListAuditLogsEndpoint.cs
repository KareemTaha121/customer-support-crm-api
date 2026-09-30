using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Contracts.Audit;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CustomerSupportCrm.Application.Features.AuditLogs.List;

internal sealed class ListAuditLogsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/audit-logs", async ([AsParameters] ListAuditLogsQuery query, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(query, cancellationToken);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.AuditView)
            .WithName("ListAuditLogs")
            .WithTags("Audit")
            .Produces<ApiResponse<IReadOnlyList<AuditLogResponse>>>();
}
