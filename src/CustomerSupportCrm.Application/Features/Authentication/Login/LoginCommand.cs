using CustomerSupportCrm.Application.Features.Authentication.Common;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Authentication.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthenticatedSession>;
