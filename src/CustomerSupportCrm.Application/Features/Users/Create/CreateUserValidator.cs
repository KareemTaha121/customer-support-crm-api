using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Users.Create;

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength).EmailAddress();
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(User.DisplayNameMaxLength);
        RuleFor(command => command.Password).ValidPassword();
        RuleFor(command => command.RoleIds).NotNull();
    }
}
