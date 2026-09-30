using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.Enable;

/// <summary>Re-activates the account and clears any lockout.</summary>
public sealed record EnableUserCommand(Guid UserId) : IRequest<UserResponse>;
