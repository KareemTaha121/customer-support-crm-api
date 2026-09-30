using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.Delete;

public sealed record DeleteRoleCommand(Guid RoleId) : IRequest;
