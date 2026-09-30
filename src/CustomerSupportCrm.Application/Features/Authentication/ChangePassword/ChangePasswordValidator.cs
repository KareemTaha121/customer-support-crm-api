using CustomerSupportCrm.Application.Common.Validation;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Authentication.ChangePassword;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(CommonRules.PasswordMaxLength);
        RuleFor(command => command.NewPassword).ValidPassword().NotEqual(command => command.CurrentPassword);
    }
}
