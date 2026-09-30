using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Roles.List;

internal sealed class ListRolesHandler(IApplicationDbContext db) : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleResponse>>
{
    public async Task<IReadOnlyList<RoleResponse>> Handle(ListRolesQuery request, CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).ProjectToResponse(db).ToListAsync(cancellationToken);
}
