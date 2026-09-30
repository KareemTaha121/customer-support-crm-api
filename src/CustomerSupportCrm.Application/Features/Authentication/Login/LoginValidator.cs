using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Domain.Shared;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Authentication.Login;

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(CommonRules.PasswordMaxLength);
    }
}
