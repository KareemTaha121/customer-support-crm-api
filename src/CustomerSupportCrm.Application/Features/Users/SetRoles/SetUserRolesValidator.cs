using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Users.SetRoles;

internal sealed class SetUserRolesValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(command => command.RoleIds).NotNull();
    }
}
