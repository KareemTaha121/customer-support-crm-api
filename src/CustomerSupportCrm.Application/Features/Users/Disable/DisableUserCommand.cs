using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.Disable;

public sealed record DisableUserCommand(Guid UserId) : IRequest<UserResponse>;
