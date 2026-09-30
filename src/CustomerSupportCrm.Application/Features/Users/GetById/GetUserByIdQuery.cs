using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.GetById;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserResponse>;
