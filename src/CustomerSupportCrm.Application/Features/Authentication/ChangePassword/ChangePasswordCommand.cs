using MediatR;

namespace CustomerSupportCrm.Application.Features.Authentication.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest;
