using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Domain.Audit;
using FluentValidation;

namespace CustomerSupportCrm.Application.Features.AuditLogs.List;

internal sealed class ListAuditLogsValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsValidator()
    {
        RuleFor(query => query.Page).ValidPage();
        RuleFor(query => query.PageSize).ValidPageSize();
        RuleFor(query => query.Action).MaximumLength(AuditLog.ActionMaxLength);
        RuleFor(query => query.EntityType).MaximumLength(AuditLog.EntityTypeMaxLength);
        RuleFor(query => query.EntityId).MaximumLength(AuditLog.EntityIdMaxLength);
        RuleFor(query => query.To).GreaterThanOrEqualTo(query => query.From).When(query => query.From is not null && query.To is not null);
    }
}
