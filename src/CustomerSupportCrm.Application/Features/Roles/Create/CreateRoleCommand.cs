using CustomerSupportCrm.Application.Features.Roles.Common;
using CustomerSupportCrm.Contracts.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.Create;

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions)
    : IRequest<RoleResponse>, IRoleDefinition;
