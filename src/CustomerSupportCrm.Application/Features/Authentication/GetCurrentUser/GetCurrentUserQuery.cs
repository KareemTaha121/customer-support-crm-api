using CustomerSupportCrm.Contracts.Authentication;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Authentication.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserResponse>;
