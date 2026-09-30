using System.Globalization;
using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Sla;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Sla;

public static class AutomationErrors
{
    public const string PolicyNotFound = "SLA_POLICY_NOT_FOUND";
    public const string RuleNotFound = "RULE_NOT_FOUND";
}

internal static class SlaMapping
{
    public static SlaPolicyResponse ToResponse(SlaPolicy p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.IsActive,
        p.IsDefault,
        p.CategoryId,
        p.DepartmentId,
        p.BusinessHoursOnly,
        p.WorkDays,
        p.WorkStart.ToString("HH:mm", CultureInfo.InvariantCulture),
        p.WorkEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
        [.. p.Targets.OrderBy(t => t.Priority).Select(t => new SlaTargetDto(t.Priority.ToString(), t.FirstResponseMinutes, t.ResolutionMinutes))]);

    public static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : fallback;

    public static T? ParseEnum<T>(string? value)
        where T : struct, Enum => value is null ? null : Enum.Parse<T>(value);
}

// ---------- SLA policies ----------

public sealed record ListSlaPoliciesQuery : IRequest<IReadOnlyList<SlaPolicyResponse>>;

internal sealed class ListSlaPoliciesHandler(IApplicationDbContext db) : IRequestHandler<ListSlaPoliciesQuery, IReadOnlyList<SlaPolicyResponse>>
{
    public async Task<IReadOnlyList<SlaPolicyResponse>> Handle(ListSlaPoliciesQuery request, CancellationToken cancellationToken) =>
        [.. (await db.SlaPolicies.AsNoTracking().Include(p => p.Targets).OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken)).Select(SlaMapping.ToResponse)];
}

public sealed record SaveSlaPolicyCommand(Guid? PolicyId, SlaPolicyRequest Policy) : IRequest<SlaPolicyResponse>;

internal sealed class SaveSlaPolicyValidator : AbstractValidator<SaveSlaPolicyCommand>
{
    public SaveSlaPolicyValidator()
    {
        RuleFor(c => c.Policy.Name).NotEmpty().MaximumLength(SlaPolicy.NameMaxLength);
        RuleFor(c => c.Policy.Description).MaximumLength(1000);
        RuleFor(c => c.Policy.Targets).NotEmpty();
        RuleForEach(c => c.Policy.Targets).ChildRules(t =>
        {
            t.RuleFor(x => x.Priority).IsEnumName(typeof(TicketPriority), caseSensitive: true);
            t.RuleFor(x => x.FirstResponseMinutes).InclusiveBetween(1, 60 * 24 * 60);
            t.RuleFor(x => x.ResolutionMinutes).InclusiveBetween(1, 60 * 24 * 365);
        });
        RuleFor(c => c.Policy.WorkStart).Matches("^([01]\\d|2[0-3]):[0-5]\\d$").When(c => c.Policy.WorkStart is not null);
        RuleFor(c => c.Policy.WorkEnd).Matches("^([01]\\d|2[0-3]):[0-5]\\d$").When(c => c.Policy.WorkEnd is not null);
    }
}

internal sealed class SaveSlaPolicyHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveSlaPolicyCommand, SlaPolicyResponse>
{
    public async Task<SlaPolicyResponse> Handle(SaveSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var input = request.Policy;
        SlaPolicy policy;
        if (request.PolicyId is { } id)
        {
            policy = await db.SlaPolicies.Include(p => p.Targets).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
                ?? throw new NotFoundException(AutomationErrors.PolicyNotFound, "The SLA policy was not found.");
        }
        else
        {
            policy = SlaPolicy.Create(input.Name);
            db.SlaPolicies.Add(policy);
        }

        policy.Update(
            input.Name,
            input.Description,
            input.IsActive,
            input.IsDefault,
            input.CategoryId,
            input.DepartmentId,
            input.BusinessHoursOnly,
            input.WorkDays ?? [0, 1, 2, 3, 4],
            SlaMapping.ParseTime(input.WorkStart, new TimeOnly(8, 0)),
            SlaMapping.ParseTime(input.WorkEnd, new TimeOnly(17, 0)),
            input.Targets.Select(t => (Enum.Parse<TicketPriority>(t.Priority), t.FirstResponseMinutes, t.ResolutionMinutes)));

        // Only one default policy.
        if (input.IsDefault)
        {
            foreach (var other in await db.SlaPolicies.Where(p => p.IsDefault && p.Id != policy.Id).ToListAsync(cancellationToken))
            {
                other.ClearDefault();
            }
        }

        audit.Record(request.PolicyId is null ? "sla_policies.created" : "sla_policies.updated", "SlaPolicy", policy.Id.ToString(), newValues: input);
        await db.SaveChangesAsync(cancellationToken);
        return SlaMapping.ToResponse(policy);
    }
}

public sealed record DeleteSlaPolicyCommand(Guid PolicyId) : IRequest;

/// <summary>Existing tickets keep their computed due dates; the policy reference is cleared.</summary>
internal sealed class DeleteSlaPolicyHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<DeleteSlaPolicyCommand>
{
    public async Task Handle(DeleteSlaPolicyCommand request, CancellationToken cancellationToken)
    {
        var policy = await db.SlaPolicies.SingleOrDefaultAsync(p => p.Id == request.PolicyId, cancellationToken)
            ?? throw new NotFoundException(AutomationErrors.PolicyNotFound, "The SLA policy was not found.");
        db.SlaPolicies.Remove(policy);
        audit.Record("sla_policies.deleted", "SlaPolicy", policy.Id.ToString(), oldValues: new { policy.Name });
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Assignment rules ----------

public sealed record ListAssignmentRulesQuery : IRequest<IReadOnlyList<AssignmentRuleResponse>>;

internal sealed class ListAssignmentRulesHandler(IApplicationDbContext db) : IRequestHandler<ListAssignmentRulesQuery, IReadOnlyList<AssignmentRuleResponse>>
{
    public async Task<IReadOnlyList<AssignmentRuleResponse>> Handle(ListAssignmentRulesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.AssignmentRules.AsNoTracking()
            .OrderBy(r => r.Order)
            .Select(r => new { Rule = r, AgentName = db.Users.Where(u => u.Id == r.AgentId).Select(u => u.DisplayName).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => AssignmentRuleMapping.ToResponse(r.Rule, r.AgentName))];
    }
}

internal static class AssignmentRuleMapping
{
    public static AssignmentRuleResponse ToResponse(AssignmentRule r, string? agentName) => new(
        r.Id,
        r.Name,
        r.IsActive,
        r.Order,
        r.MatchCategoryId,
        r.MatchDepartmentId,
        r.MatchChannel?.ToString(),
        r.MatchPriority?.ToString(),
        r.MatchKeyword,
        r.SetDepartmentId,
        r.SetPriority?.ToString(),
        r.Strategy.ToString(),
        r.AgentId?.Value,
        agentName);
}

public sealed record SaveAssignmentRuleCommand(Guid? RuleId, AssignmentRuleRequest Rule) : IRequest<AssignmentRuleResponse>;

internal sealed class SaveAssignmentRuleValidator : AbstractValidator<SaveAssignmentRuleCommand>
{
    public SaveAssignmentRuleValidator()
    {
        RuleFor(c => c.Rule.Name).NotEmpty().MaximumLength(AssignmentRule.NameMaxLength);
        RuleFor(c => c.Rule.Strategy).NotEmpty().IsEnumName(typeof(AssignmentStrategy));
        RuleFor(c => c.Rule.MatchChannel).Must(v => v is null || Enum.TryParse<TicketChannel>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.MatchPriority).Must(v => v is null || Enum.TryParse<TicketPriority>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.SetPriority).Must(v => v is null || Enum.TryParse<TicketPriority>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.MatchKeyword).MaximumLength(200);
    }
}

internal sealed class SaveAssignmentRuleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveAssignmentRuleCommand, AssignmentRuleResponse>
{
    public async Task<AssignmentRuleResponse> Handle(SaveAssignmentRuleCommand request, CancellationToken cancellationToken)
    {
        var input = request.Rule;
        AssignmentRule rule;
        if (request.RuleId is { } id)
        {
            rule = await db.AssignmentRules.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(AutomationErrors.RuleNotFound, "The rule was not found.");
        }
        else
        {
            rule = AssignmentRule.Create();
            db.AssignmentRules.Add(rule);
        }

        rule.Update(
            input.Name,
            input.IsActive,
            input.Order,
            input.MatchCategoryId,
            input.MatchDepartmentId,
            SlaMapping.ParseEnum<TicketChannel>(input.MatchChannel),
            SlaMapping.ParseEnum<TicketPriority>(input.MatchPriority),
            input.MatchKeyword,
            input.SetDepartmentId,
            SlaMapping.ParseEnum<TicketPriority>(input.SetPriority),
            Enum.Parse<AssignmentStrategy>(input.Strategy),
            input.AgentId is { } agent ? new UserId(agent) : null);

        audit.Record(request.RuleId is null ? "assignment_rules.created" : "assignment_rules.updated", "AssignmentRule", rule.Id.ToString(), newValues: input);
        await db.SaveChangesAsync(cancellationToken);

        var agentName = rule.AgentId is { } a ? await db.Users.Where(u => u.Id == a).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) : null;
        return AssignmentRuleMapping.ToResponse(rule, agentName);
    }
}

// ---------- Escalation rules ----------

public sealed record ListEscalationRulesQuery : IRequest<IReadOnlyList<EscalationRuleResponse>>;

internal sealed class ListEscalationRulesHandler(IApplicationDbContext db) : IRequestHandler<ListEscalationRulesQuery, IReadOnlyList<EscalationRuleResponse>>
{
    public async Task<IReadOnlyList<EscalationRuleResponse>> Handle(ListEscalationRulesQuery request, CancellationToken cancellationToken) =>
        [.. (await db.EscalationRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken)).Select(EscalationRuleMapping.ToResponse)];
}

internal static class EscalationRuleMapping
{
    public static EscalationRuleResponse ToResponse(EscalationRule r) => new(
        r.Id,
        r.Name,
        r.IsActive,
        r.Trigger.ToString(),
        r.Target?.ToString(),
        r.AfterMinutes,
        r.MatchPriority?.ToString(),
        r.MatchDepartmentId,
        r.EscalateTicket,
        r.RaisePriorityTo?.ToString(),
        r.ReassignToAgentId?.Value,
        r.NotifyAssignee,
        r.NotifyManagers,
        r.NotifyUserIds);
}

public sealed record SaveEscalationRuleCommand(Guid? RuleId, EscalationRuleRequest Rule) : IRequest<EscalationRuleResponse>;

internal sealed class SaveEscalationRuleValidator : AbstractValidator<SaveEscalationRuleCommand>
{
    public SaveEscalationRuleValidator()
    {
        RuleFor(c => c.Rule.Name).NotEmpty().MaximumLength(EscalationRule.NameMaxLength);
        RuleFor(c => c.Rule.Trigger).NotEmpty().IsEnumName(typeof(EscalationTrigger));
        RuleFor(c => c.Rule.Target).Must(v => v is null || Enum.TryParse<SlaTarget>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.MatchPriority).Must(v => v is null || Enum.TryParse<TicketPriority>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.RaisePriorityTo).Must(v => v is null || Enum.TryParse<TicketPriority>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Rule.AfterMinutes).InclusiveBetween(1, 60 * 24 * 30).When(c => c.Rule.AfterMinutes is not null);
    }
}

internal sealed class SaveEscalationRuleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveEscalationRuleCommand, EscalationRuleResponse>
{
    public async Task<EscalationRuleResponse> Handle(SaveEscalationRuleCommand request, CancellationToken cancellationToken)
    {
        var input = request.Rule;
        EscalationRule rule;
        if (request.RuleId is { } id)
        {
            rule = await db.EscalationRules.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(AutomationErrors.RuleNotFound, "The rule was not found.");
        }
        else
        {
            rule = EscalationRule.Create();
            db.EscalationRules.Add(rule);
        }

        rule.Update(
            input.Name,
            input.IsActive,
            Enum.Parse<EscalationTrigger>(input.Trigger),
            SlaMapping.ParseEnum<SlaTarget>(input.Target),
            input.AfterMinutes,
            SlaMapping.ParseEnum<TicketPriority>(input.MatchPriority),
            input.MatchDepartmentId,
            input.EscalateTicket,
            SlaMapping.ParseEnum<TicketPriority>(input.RaisePriorityTo),
            input.ReassignToAgentId is { } agent ? new UserId(agent) : null,
            input.NotifyAssignee,
            input.NotifyManagers,
            input.NotifyUserIds ?? []);

        audit.Record(request.RuleId is null ? "escalation_rules.created" : "escalation_rules.updated", "EscalationRule", rule.Id.ToString(), newValues: input);
        await db.SaveChangesAsync(cancellationToken);
        return EscalationRuleMapping.ToResponse(rule);
    }
}

/// <param name="Kind">"assignment" or "escalation".</param>
public sealed record DeleteAutomationRuleCommand(string Kind, Guid RuleId) : IRequest;

internal sealed class DeleteAutomationRuleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<DeleteAutomationRuleCommand>
{
    public async Task Handle(DeleteAutomationRuleCommand request, CancellationToken cancellationToken)
    {
        if (request.Kind == "assignment")
        {
            var rule = await db.AssignmentRules.SingleOrDefaultAsync(r => r.Id == request.RuleId, cancellationToken)
                ?? throw new NotFoundException(AutomationErrors.RuleNotFound, "The rule was not found.");
            db.AssignmentRules.Remove(rule);
        }
        else
        {
            var rule = await db.EscalationRules.SingleOrDefaultAsync(r => r.Id == request.RuleId, cancellationToken)
                ?? throw new NotFoundException(AutomationErrors.RuleNotFound, "The rule was not found.");
            db.EscalationRules.Remove(rule);
        }

        audit.Record($"{request.Kind}_rules.deleted", "AutomationRule", request.RuleId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Endpoints ----------

internal sealed class SlaAdministrationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var sla = app.MapGroup("/sla-policies").WithTags("SLA & Automation");
        sla.MapGet("/", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListSlaPoliciesQuery(), ct)))
            .WithName("ListSlaPolicies")
            .Produces<ApiResponse<IReadOnlyList<SlaPolicyResponse>>>();
        sla.MapPost("/", async (SlaPolicyRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/sla-policies", await sender.Send(new SaveSlaPolicyCommand(null, request), ct)))
            .RequireAuthorization(Permissions.SlaManage)
            .WithName("CreateSlaPolicy")
            .Produces<ApiResponse<SlaPolicyResponse>>(StatusCodes.Status201Created);
        sla.MapPut("/{id:guid}", async (Guid id, SlaPolicyRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveSlaPolicyCommand(id, request), ct)))
            .RequireAuthorization(Permissions.SlaManage)
            .WithName("UpdateSlaPolicy")
            .Produces<ApiResponse<SlaPolicyResponse>>();
        sla.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteSlaPolicyCommand(id), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.SlaManage)
            .WithName("DeleteSlaPolicy")
            .Produces<ApiResponse<object?>>();

        var assignment = app.MapGroup("/automation/assignment-rules").WithTags("SLA & Automation").RequireAuthorization(Permissions.AutomationManage);
        assignment.MapGet("/", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListAssignmentRulesQuery(), ct)))
            .WithName("ListAssignmentRules")
            .Produces<ApiResponse<IReadOnlyList<AssignmentRuleResponse>>>();
        assignment.MapPost("/", async (AssignmentRuleRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/automation/assignment-rules", await sender.Send(new SaveAssignmentRuleCommand(null, request), ct)))
            .WithName("CreateAssignmentRule")
            .Produces<ApiResponse<AssignmentRuleResponse>>(StatusCodes.Status201Created);
        assignment.MapPut("/{id:guid}", async (Guid id, AssignmentRuleRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveAssignmentRuleCommand(id, request), ct)))
            .WithName("UpdateAssignmentRule")
            .Produces<ApiResponse<AssignmentRuleResponse>>();
        assignment.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteAutomationRuleCommand("assignment", id), ct);
                return ApiResults.Success();
            })
            .WithName("DeleteAssignmentRule")
            .Produces<ApiResponse<object?>>();

        var escalation = app.MapGroup("/automation/escalation-rules").WithTags("SLA & Automation").RequireAuthorization(Permissions.AutomationManage);
        escalation.MapGet("/", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListEscalationRulesQuery(), ct)))
            .WithName("ListEscalationRules")
            .Produces<ApiResponse<IReadOnlyList<EscalationRuleResponse>>>();
        escalation.MapPost("/", async (EscalationRuleRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/automation/escalation-rules", await sender.Send(new SaveEscalationRuleCommand(null, request), ct)))
            .WithName("CreateEscalationRule")
            .Produces<ApiResponse<EscalationRuleResponse>>(StatusCodes.Status201Created);
        escalation.MapPut("/{id:guid}", async (Guid id, EscalationRuleRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveEscalationRuleCommand(id, request), ct)))
            .WithName("UpdateEscalationRule")
            .Produces<ApiResponse<EscalationRuleResponse>>();
        escalation.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteAutomationRuleCommand("escalation", id), ct);
                return ApiResults.Success();
            })
            .WithName("DeleteEscalationRule")
            .Produces<ApiResponse<object?>>();
    }
}
