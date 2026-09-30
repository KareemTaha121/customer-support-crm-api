using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.Create;

public sealed record CreateUserCommand(string Email, string DisplayName, string Password, IReadOnlyList<Guid> RoleIds, IReadOnlyList<UserScopeRequest>? Scopes = null)
    : IRequest<UserResponse>;
