using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Roles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Roles.Delete;

/// <summary>Deletes an unassigned, non-system role. Unassign users first.</summary>
internal sealed class DeleteRoleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var roleId = new RoleId(request.RoleId);
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw RoleQueries.NotFound();

        role.EnsureCanBeDeleted();

        if (await db.Users.AnyAsync(u => u.Roles.Any(membership => membership.RoleId == roleId), cancellationToken))
        {
            throw new ConflictException(RoleErrors.RoleInUse, "The role is assigned to users.");
        }

        db.Roles.Remove(role);
        audit.Record(
            AuditActions.RoleDeleted,
            AuditEntityTypes.Role,
            role.Id.ToString(),
            oldValues: new { role.Name, role.Description, permissions = role.PermissionCodes });
        await db.SaveChangesAsync(cancellationToken);
    }
}
