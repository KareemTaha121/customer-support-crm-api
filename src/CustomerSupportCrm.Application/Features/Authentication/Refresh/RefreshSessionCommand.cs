using CustomerSupportCrm.Application.Features.Authentication.Common;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Authentication.Refresh;

/// <param name="RefreshToken">The value from the refresh cookie, if present.</param>
public sealed record RefreshSessionCommand(string? RefreshToken) : IRequest<AuthenticatedSession>;
