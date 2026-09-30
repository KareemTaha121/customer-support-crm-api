using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Contracts.Common;
using FluentValidation;

namespace CustomerSupportCrm.Application.Common.Validation;

public static class CommonRules
{
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;

    /// <summary>Length-based policy (NIST SP 800-63B); no composition rules.</summary>
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Length(PasswordMinLength, PasswordMaxLength);

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(1);

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, PaginationExtensions.MaxPageSize);

    public static IRuleBuilderOptions<T, string?> ValidSortDirection<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(direction => direction is null or "asc" or "desc").WithErrorCode(ErrorCodes.InvalidValue);

    public static IRuleBuilderOptions<T, string?> OneOf<T>(this IRuleBuilder<T, string?> rule, params string[] allowed) =>
        rule.Must(value => value is null || allowed.Contains(value, StringComparer.Ordinal)).WithErrorCode(ErrorCodes.InvalidValue);
}
