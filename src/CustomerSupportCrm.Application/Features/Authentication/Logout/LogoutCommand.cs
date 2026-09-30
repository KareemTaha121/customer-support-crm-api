using MediatR;

namespace CustomerSupportCrm.Application.Features.Authentication.Logout;

/// <param name="RefreshToken">The value from the refresh cookie, if present.</param>
public sealed record LogoutCommand(string? RefreshToken) : IRequest;
