using CustomerSupportCrm.Domain.Roles;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Roles.Common;

/// <summary>The editable fields of a role, shared by create and update.</summary>
public interface IRoleDefinition
{
    string Name { get; }

    string? Description { get; }

    IReadOnlyList<string> Permissions { get; }
}

/// <summary>Field rules for role input. Business rules (system roles, uniqueness) live elsewhere.</summary>
internal abstract class RoleDefinitionValidator<T> : AbstractValidator<T>
    where T : IRoleDefinition
{
    protected RoleDefinitionValidator()
    {
        RuleFor(role => role.Name).NotEmpty().MaximumLength(Role.NameMaxLength);
        RuleFor(role => role.Description).MaximumLength(Role.DescriptionMaxLength);
        RuleFor(role => role.Permissions).NotNull();
        RuleForEach(role => role.Permissions).Must(Permissions.IsKnown).WithErrorCode(Role.UnknownPermissionCode);
    }
}
