using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Roles.Update;

/// <summary>
/// Permission changes reach signed-in users on their next token refresh (at most one
/// access-token lifetime).
/// </summary>
internal sealed class UpdateRoleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<UpdateRoleCommand, RoleResponse>
{
    public async Task<RoleResponse> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var roleId = new RoleId(request.RoleId);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw RoleQueries.NotFound();

        if (await RoleQueries.NameTakenAsync(db, request.Name, excluding: roleId, cancellationToken))
        {
            throw new ConflictException(RoleErrors.RoleNameTaken, "A role with this name already exists.");
        }

        var before = new { role.Name, role.Description, permissions = role.PermissionCodes };
        role.Update(request.Name, request.Description, request.Permissions);

        audit.Record(
            AuditActions.RoleUpdated,
            AuditEntityTypes.Role,
            role.Id.ToString(),
            oldValues: before,
            newValues: new { role.Name, role.Description, permissions = role.PermissionCodes });
        await db.SaveChangesAsync(cancellationToken);

        return await RoleQueries.GetResponseAsync(db, role.Id, cancellationToken);
    }
}
