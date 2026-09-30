using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.Create;

internal sealed class CreateRoleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<CreateRoleCommand, RoleResponse>
{
    public async Task<RoleResponse> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (await RoleQueries.NameTakenAsync(db, request.Name, excluding: null, cancellationToken))
        {
            throw new ConflictException(RoleErrors.RoleNameTaken, "A role with this name already exists.");
        }

        var role = Role.Create(request.Name, request.Description, request.Permissions);
        db.Roles.Add(role);

        audit.Record(
            AuditActions.RoleCreated,
            AuditEntityTypes.Role,
            role.Id.ToString(),
            newValues: new { role.Name, role.Description, permissions = role.PermissionCodes });
        await db.SaveChangesAsync(cancellationToken);

        return await RoleQueries.GetResponseAsync(db, role.Id, cancellationToken);
    }
}
