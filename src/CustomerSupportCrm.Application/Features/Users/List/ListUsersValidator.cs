using CustomerSupportCrm.Application.Common.Validation;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.Users.List;

internal sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
        RuleFor(query => query.Search).MaximumLength(200);
        RuleFor(query => query.Status).OneOf("Active", "Disabled");
        RuleFor(query => query.SortBy).OneOf("displayName", "email", "createdAt", "lastLoginAt");
        RuleFor(query => query.SortDirection).ValidSortDirection();
    }
}
