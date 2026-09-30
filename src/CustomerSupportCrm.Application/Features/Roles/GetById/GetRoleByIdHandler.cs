using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.GetById;

internal sealed class GetRoleByIdHandler(IApplicationDbContext db) : IRequestHandler<GetRoleByIdQuery, RoleResponse>
{
    public Task<RoleResponse> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken) =>
        RoleQueries.GetResponseAsync(db, new RoleId(request.RoleId), cancellationToken);
}
