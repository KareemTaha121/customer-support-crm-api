using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.Update;

public sealed record UpdateRoleCommand(Guid RoleId, string Name, string? Description, IReadOnlyList<string> Permissions)
    : IRequest<RoleResponse>, IRoleDefinition;
