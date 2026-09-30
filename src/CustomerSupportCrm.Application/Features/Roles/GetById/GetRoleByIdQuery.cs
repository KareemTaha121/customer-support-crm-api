using CustomerSupportCrm.Contracts.Roles;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Roles.GetById;

public sealed record GetRoleByIdQuery(Guid RoleId) : IRequest<RoleResponse>;
