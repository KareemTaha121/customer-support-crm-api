using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.SetRoles;

/// <summary>Replaces the user's role set.</summary>
public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest<UserResponse>;
