using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Application.Features.Notifications;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Dashboard;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Dashboard;

// ---------- Dashboard ----------

public sealed record GetAgentDashboardQuery : IRequest<AgentDashboardResponse>;

/// <summary>Only the counts and short lists the agent home screen shows; never full aggregates.</summary>
internal sealed class GetAgentDashboardHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetAgentDashboardQuery, AgentDashboardResponse>
{
    private const int ListSize = 10;

    public async Task<AgentDashboardResponse> Handle(GetAgentDashboardQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var now = time.GetUtcNow();
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var scope = await scopes.GetAsync(cancellationToken);

        var scoped = db.Tickets.AsNoTracking().WhereInScope(scope);
        var active = scoped.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);
        var mine = active.Where(t => t.AssignedAgentId == me);
        var atRisk = active.Where(t => t.FirstResponseBreached || t.ResolutionBreached || t.FirstResponseWarnedAt != null || t.ResolutionWarnedAt != null);
        var tasks = db.AgentTasks.AsNoTracking().Where(t => t.AssigneeId == me && t.CompletedAt == null);

        var counts = new AgentDashboardCounts(
            await mine.CountAsync(cancellationToken),
            await mine.CountAsync(t => t.Status == TicketStatus.PendingCustomer, cancellationToken),
            await atRisk.CountAsync(t => t.AssignedAgentId == me, cancellationToken),
            await scoped.CountAsync(t => t.AssignedAgentId == me && t.ResolvedAt >= todayStart, cancellationToken),
            await active.CountAsync(t => t.AssignedAgentId == null, cancellationToken),
            await active.CountAsync(t => t.Status == TicketStatus.Escalated, cancellationToken),
            await tasks.CountAsync(cancellationToken),
            await tasks.CountAsync(t => t.DueAt < now, cancellationToken),
            await db.Notifications.CountAsync(n => n.RecipientId == me && n.ReadAt == null, cancellationToken));

        var myTickets = await TicketQueries.ProjectListAsync(mine.OrderBy(t => t.ResolutionDueAt == null).ThenBy(t => t.ResolutionDueAt).Take(ListSize), db, cancellationToken);
        var slaAtRisk = await TicketQueries.ProjectListAsync(atRisk.OrderBy(t => t.ResolutionDueAt).Take(ListSize), db, cancellationToken);
        var escalations = await TicketQueries.ProjectListAsync(active.Where(t => t.Status == TicketStatus.Escalated).OrderByDescending(t => t.EscalatedAt).Take(ListSize), db, cancellationToken);

        var recentCustomers = await db.Tickets.AsNoTracking()
            .Where(t => t.AssignedAgentId == me)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Last = g.Max(t => t.UpdatedAt ?? t.CreatedAt) })
            .OrderByDescending(g => g.Last)
            .Take(8)
            .Join(db.Customers, g => g.CustomerId, c => c.Id, (g, c) => new RecentCustomerResponse(c.Id, c.Number, c.Name, g.Last))
            .ToListAsync(cancellationToken);

        var myTasks = await TaskQueries.Project(tasks.OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).Take(ListSize), db).ToListAsync(cancellationToken);

        return new AgentDashboardResponse(counts, myTickets, slaAtRisk, escalations, recentCustomers, myTasks);
    }
}

// ---------- Tasks ----------

internal static class TaskQueries
{
    public const string NotFoundCode = "TASK_NOT_FOUND";

    public static IQueryable<TaskResponse> Project(IQueryable<AgentTask> tasks, IApplicationDbContext db) =>
        tasks.Select(t => new TaskResponse(
            t.Id,
            t.Title,
            t.Notes,
            t.AssigneeId.Value,
            db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.DisplayName).FirstOrDefault() ?? string.Empty,
            t.TicketId,
            db.Tickets.Where(x => x.Id == t.TicketId).Select(x => x.Number).FirstOrDefault(),
            t.CustomerId,
            db.Customers.Where(c => c.Id == t.CustomerId).Select(c => c.Name).FirstOrDefault(),
            t.DueAt,
            t.RemindAt,
            t.CompletedAt,
            t.CreatedAt));

    /// <summary>
    /// A task's ticket/customer must exist and be in the caller's branch/department scope;
    /// otherwise 404 with the ticket/customer code, so out-of-scope records are not disclosed.
    /// </summary>
    public static async Task EnsureReferencesInScopeAsync(IApplicationDbContext db, AccessScope scope, Guid? ticketId, Guid? customerId, CancellationToken cancellationToken)
    {
        if (ticketId is { } t && !await db.Tickets.AsNoTracking().Where(x => x.Id == t).WhereInScope(scope).AnyAsync(cancellationToken))
        {
            throw new NotFoundException(TicketErrors.TicketNotFound, "The ticket was not found.");
        }

        if (customerId is { } c && !await db.Customers.AsNoTracking().Where(x => x.Id == c).WhereInScope(scope).AnyAsync(cancellationToken))
        {
            throw new NotFoundException(CustomerErrors.CustomerNotFound, "The customer was not found.");
        }
    }
}

/// <param name="Status">open (default), completed or all.</param>
/// <param name="Assignee">
/// "me" or a user id (requires tickets.assign). When omitted: the caller's own tasks, except that
/// tickets.assign holders filtering by an in-scope ticket/customer see every assignee's tasks on it.
/// </param>
public sealed record ListTasksQuery(string? Status = null, string? Assignee = null, Guid? TicketId = null, Guid? CustomerId = null) : IRequest<IReadOnlyList<TaskResponse>>;

internal sealed class ListTasksHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser) : IRequestHandler<ListTasksQuery, IReadOnlyList<TaskResponse>>
{
    public async Task<IReadOnlyList<TaskResponse>> Handle(ListTasksQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var canSeeOthers = currentUser.HasPermission(Permissions.TicketsAssign);
        var byRecord = request.TicketId is not null || request.CustomerId is not null;
        if (byRecord)
        {
            await TaskQueries.EnsureReferencesInScopeAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, request.CustomerId, cancellationToken);
        }

        var query = db.AgentTasks.AsNoTracking();

        if (request.Assignee is { } assignee && assignee != "me" && Guid.TryParse(assignee, out var otherId))
        {
            if (!canSeeOthers)
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "You can only see your own tasks.");
            }

            var other = new UserId(otherId);
            query = query.Where(t => t.AssigneeId == other);
        }
        else if (request.Assignee == "me" || !byRecord || !canSeeOthers)
        {
            query = query.Where(t => t.AssigneeId == me);
        }

        if (request.TicketId is { } ticketId)
        {
            query = query.Where(t => t.TicketId == ticketId);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(t => t.CustomerId == customerId);
        }

        query = request.Status switch
        {
            "completed" => query.Where(t => t.CompletedAt != null),
            "all" => query,
            _ => query.Where(t => t.CompletedAt == null),
        };

        return await TaskQueries.Project(query.OrderBy(t => t.CompletedAt != null).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).Take(200), db).ToListAsync(cancellationToken);
    }
}

public sealed record SaveTaskCommand(Guid? TaskId, TaskRequest Task) : IRequest<TaskResponse>;

internal sealed class SaveTaskValidator : AbstractValidator<SaveTaskCommand>
{
    public SaveTaskValidator()
    {
        RuleFor(c => c.Task.Title).NotEmpty().MaximumLength(AgentTask.TitleMaxLength);
        RuleFor(c => c.Task.Notes).MaximumLength(4000);
    }
}

internal sealed class SaveTaskHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser) : IRequestHandler<SaveTaskCommand, TaskResponse>
{
    public async Task<TaskResponse> Handle(SaveTaskCommand request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var input = request.Task;
        var assignee = input.AssigneeId is { } a ? new UserId(a) : me;
        if (assignee != me && !currentUser.HasPermission(Permissions.TicketsAssign))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You can only create tasks for yourself.");
        }

        AgentTask task;
        if (request.TaskId is { } id)
        {
            task = await LoadOwnAsync(db, currentUser, id, cancellationToken);

            // Only new links are checked, so a task stays editable after its ticket/customer moves out of scope.
            await TaskQueries.EnsureReferencesInScopeAsync(
                db,
                await scopes.GetAsync(cancellationToken),
                input.TicketId != task.TicketId ? input.TicketId : null,
                input.CustomerId != task.CustomerId ? input.CustomerId : null,
                cancellationToken);
            task.Update(input.Title, input.Notes, input.TicketId, input.CustomerId, input.DueAt, input.RemindAt);
            task.Reassign(assignee);
        }
        else
        {
            await TaskQueries.EnsureReferencesInScopeAsync(db, await scopes.GetAsync(cancellationToken), input.TicketId, input.CustomerId, cancellationToken);
            task = AgentTask.Create(assignee, input.Title, input.Notes, input.TicketId, input.CustomerId, input.DueAt, input.RemindAt);
            db.AgentTasks.Add(task);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await TaskQueries.Project(db.AgentTasks.AsNoTracking().Where(t => t.Id == task.Id), db).SingleAsync(cancellationToken);
    }

    /// <summary>Tasks are editable by their assignee, their creator, or anyone with tickets.assign.</summary>
    public static async Task<AgentTask> LoadOwnAsync(IApplicationDbContext db, ICurrentUser currentUser, Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException(TaskQueries.NotFoundCode, "The task was not found.");
        var me = currentUser.UserId;
        if (task.AssigneeId != me && task.CreatedBy != me.Value && !currentUser.HasPermission(Permissions.TicketsAssign))
        {
            throw new NotFoundException(TaskQueries.NotFoundCode, "The task was not found.");
        }

        return task;
    }
}

/// <param name="Action">complete, reopen or delete.</param>
public sealed record ChangeTaskCommand(Guid TaskId, string Action) : IRequest;

internal sealed class ChangeTaskHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time) : IRequestHandler<ChangeTaskCommand>
{
    public async Task Handle(ChangeTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await SaveTaskHandler.LoadOwnAsync(db, currentUser, request.TaskId, cancellationToken);
        switch (request.Action)
        {
            case "complete":
                task.Complete(time.GetUtcNow());
                break;
            case "reopen":
                task.Reopen();
                break;
            default:
                db.AgentTasks.Remove(task);
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Every minute: notifies assignees of due reminders once.</summary>
public sealed record SendTaskRemindersCommand : IRequest;

internal sealed class SendTaskRemindersHandler(IApplicationDbContext db, NotificationSender notifications, TimeProvider time) : IRequestHandler<SendTaskRemindersCommand>
{
    public async Task Handle(SendTaskRemindersCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.AgentTasks
            .Where(t => t.RemindAt != null && t.RemindAt <= now && t.ReminderSentAt == null && t.CompletedAt == null)
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var task in due)
        {
            notifications.Notify(task.AssigneeId, NotificationTypes.TaskReminder, $"Reminder: {task.Title}", task.TicketId is { } ticketId ? TicketQueries.TicketLink(ticketId) : "/tasks", new { taskId = task.Id, title = task.Title });
            task.MarkReminderSent(now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Quick replies ----------

public sealed record ListQuickRepliesQuery(string? Search = null, string? Language = null) : IRequest<IReadOnlyList<QuickReplyResponse>>;

internal sealed class ListQuickRepliesHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<ListQuickRepliesQuery, IReadOnlyList<QuickReplyResponse>>
{
    public async Task<IReadOnlyList<QuickReplyResponse>> Handle(ListQuickRepliesQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var canManageShared = currentUser.HasPermission(Permissions.QuickRepliesManage);
        var query = db.QuickReplies.AsNoTracking().Where(q => q.OwnerId == null || q.OwnerId == me);

        if (request.Language is { } language)
        {
            query = query.Where(q => q.Language == language);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var upper = request.Search.Trim().TrimStart('/').ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // Translated to SQL upper().
            query = query.Where(q => q.Title.ToUpper().Contains(upper) || (q.Shortcut != null && q.Shortcut.StartsWith(upper)) || q.Body.ToUpper().Contains(upper));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var rows = await query.OrderByDescending(q => q.UsageCount).ThenBy(q => q.Title).Take(100).ToListAsync(cancellationToken);
        return [.. rows.Select(q => QuickReplyMapping.ToResponse(q, me, canManageShared))];
    }
}

internal static class QuickReplyMapping
{
    public const string NotFoundCode = "QUICK_REPLY_NOT_FOUND";

    public static QuickReplyResponse ToResponse(QuickReply q, UserId me, bool canManageShared) =>
        new(q.Id, q.Title, q.Shortcut is null ? null : "/" + q.Shortcut.ToUpperInvariant(), q.Body, q.Language, q.CategoryId, q.OwnerId is null, q.OwnerId == me || (q.OwnerId is null && canManageShared), q.UsageCount);

    public static async Task<QuickReply> LoadEditableAsync(IApplicationDbContext db, ICurrentUser currentUser, Guid id, CancellationToken cancellationToken)
    {
        var reply = await db.QuickReplies.SingleOrDefaultAsync(q => q.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFoundCode, "The quick reply was not found.");
        var me = currentUser.UserId;
        if (reply.OwnerId is { } owner && owner != me)
        {
            throw new NotFoundException(NotFoundCode, "The quick reply was not found.");
        }

        if (reply.OwnerId is null && !currentUser.HasPermission(Permissions.QuickRepliesManage))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only managers can change shared replies.");
        }

        return reply;
    }
}

public sealed record SaveQuickReplyCommand(Guid? QuickReplyId, QuickReplyRequest Reply) : IRequest<QuickReplyResponse>;

internal sealed class SaveQuickReplyValidator : AbstractValidator<SaveQuickReplyCommand>
{
    public SaveQuickReplyValidator()
    {
        RuleFor(c => c.Reply.Title).NotEmpty().MaximumLength(QuickReply.TitleMaxLength);
        RuleFor(c => c.Reply.Shortcut).MaximumLength(QuickReply.ShortcutMaxLength).Matches("^/?[A-Za-z0-9_-]+$").When(c => !string.IsNullOrEmpty(c.Reply.Shortcut));
        RuleFor(c => c.Reply.Body).NotEmpty().MaximumLength(QuickReply.BodyMaxLength);
        RuleFor(c => c.Reply.Language).Must(l => l is "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
    }
}

internal sealed class SaveQuickReplyHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<SaveQuickReplyCommand, QuickReplyResponse>
{
    public async Task<QuickReplyResponse> Handle(SaveQuickReplyCommand request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var canManageShared = currentUser.HasPermission(Permissions.QuickRepliesManage);
        var input = request.Reply;

        QuickReply reply;
        if (request.QuickReplyId is { } id)
        {
            reply = await QuickReplyMapping.LoadEditableAsync(db, currentUser, id, cancellationToken);
        }
        else
        {
            if (input.Shared && !canManageShared)
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "Only managers can create shared replies.");
            }

            reply = QuickReply.Create(input.Shared ? null : me);
            db.QuickReplies.Add(reply);
        }

        reply.Update(input.Title, input.Shortcut, input.Body, input.Language, input.CategoryId);
        await db.SaveChangesAsync(cancellationToken);
        return QuickReplyMapping.ToResponse(reply, me, canManageShared);
    }
}

public sealed record DeleteQuickReplyCommand(Guid QuickReplyId) : IRequest;

internal sealed class DeleteQuickReplyHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteQuickReplyCommand>
{
    public async Task Handle(DeleteQuickReplyCommand request, CancellationToken cancellationToken)
    {
        db.QuickReplies.Remove(await QuickReplyMapping.LoadEditableAsync(db, currentUser, request.QuickReplyId, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Fills placeholders ({{customer.name}}, {{customer.number}}, {{ticket.number}},
/// {{ticket.subject}}, {{agent.name}}) and counts the use.
/// </summary>
public sealed record RenderQuickReplyQuery(Guid QuickReplyId, Guid? TicketId) : IRequest<RenderedQuickReplyResponse>;

internal sealed class RenderQuickReplyHandler(IApplicationDbContext db, ICurrentUser currentUser, IAccessScopeProvider scopes) : IRequestHandler<RenderQuickReplyQuery, RenderedQuickReplyResponse>
{
    public async Task<RenderedQuickReplyResponse> Handle(RenderQuickReplyQuery request, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var reply = await db.QuickReplies.SingleOrDefaultAsync(q => q.Id == request.QuickReplyId && (q.OwnerId == null || q.OwnerId == me), cancellationToken)
            ?? throw new NotFoundException(QuickReplyMapping.NotFoundCode, "The quick reply was not found.");

        var body = reply.Body.Replace("{{agent.name}}", await db.Users.Where(u => u.Id == me).Select(u => u.DisplayName).FirstAsync(cancellationToken), StringComparison.Ordinal);

        if (request.TicketId is { } ticketId)
        {
            var scope = await scopes.GetAsync(cancellationToken);
            var ticket = await db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).WhereInScope(scope)
                .Select(t => new { t.Number, t.Subject, Customer = db.Customers.Where(c => c.Id == t.CustomerId).Select(c => new { c.Name, c.Number }).First() })
                .FirstOrDefaultAsync(cancellationToken);
            if (ticket is not null)
            {
                body = body
                    .Replace("{{ticket.number}}", ticket.Number, StringComparison.Ordinal)
                    .Replace("{{ticket.subject}}", ticket.Subject, StringComparison.Ordinal)
                    .Replace("{{customer.name}}", ticket.Customer.Name, StringComparison.Ordinal)
                    .Replace("{{customer.number}}", ticket.Customer.Number, StringComparison.Ordinal);
            }
        }

        reply.RecordUse();
        await db.SaveChangesAsync(cancellationToken);
        return new RenderedQuickReplyResponse(body);
    }
}

// ---------- Endpoints ----------

internal sealed class AgentWorkspaceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard/agent", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetAgentDashboardQuery(), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithTags("Agent workspace")
            .WithName("GetAgentDashboard")
            .Produces<ApiResponse<AgentDashboardResponse>>();

        var tasks = app.MapGroup("/tasks").WithTags("Agent workspace");
        tasks.MapGet("/", async ([AsParameters] ListTasksQuery query, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(query, ct)))
            .WithName("ListTasks")
            .Produces<ApiResponse<IReadOnlyList<TaskResponse>>>();
        tasks.MapPost("/", async (TaskRequest request, ISender sender, CancellationToken ct) => ApiResults.Created("/api/v1/tasks", await sender.Send(new SaveTaskCommand(null, request), ct)))
            .WithName("CreateTask")
            .Produces<ApiResponse<TaskResponse>>(StatusCodes.Status201Created);
        tasks.MapPut("/{id:guid}", async (Guid id, TaskRequest request, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new SaveTaskCommand(id, request), ct)))
            .WithName("UpdateTask")
            .Produces<ApiResponse<TaskResponse>>();
        tasks.MapPost("/{id:guid}/complete", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ChangeTaskCommand(id, "complete"), ct);
                return ApiResults.Success();
            })
            .WithName("CompleteTask")
            .Produces<ApiResponse<object?>>();
        tasks.MapPost("/{id:guid}/reopen", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ChangeTaskCommand(id, "reopen"), ct);
                return ApiResults.Success();
            })
            .WithName("ReopenTask")
            .Produces<ApiResponse<object?>>();
        tasks.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ChangeTaskCommand(id, "delete"), ct);
                return ApiResults.Success();
            })
            .WithName("DeleteTask")
            .Produces<ApiResponse<object?>>();

        var replies = app.MapGroup("/quick-replies").WithTags("Agent workspace");
        replies.MapGet("/", async ([AsParameters] ListQuickRepliesQuery query, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(query, ct)))
            .WithName("ListQuickReplies")
            .Produces<ApiResponse<IReadOnlyList<QuickReplyResponse>>>();
        replies.MapPost("/", async (QuickReplyRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/quick-replies", await sender.Send(new SaveQuickReplyCommand(null, request), ct)))
            .WithName("CreateQuickReply")
            .Produces<ApiResponse<QuickReplyResponse>>(StatusCodes.Status201Created);
        replies.MapPut("/{id:guid}", async (Guid id, QuickReplyRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveQuickReplyCommand(id, request), ct)))
            .WithName("UpdateQuickReply")
            .Produces<ApiResponse<QuickReplyResponse>>();
        replies.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteQuickReplyCommand(id), ct);
                return ApiResults.Success();
            })
            .WithName("DeleteQuickReply")
            .Produces<ApiResponse<object?>>();
        replies.MapPost("/{id:guid}/render", async (Guid id, Guid? ticketId, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new RenderQuickReplyQuery(id, ticketId), ct)))
            .WithName("RenderQuickReply")
            .Produces<ApiResponse<RenderedQuickReplyResponse>>();
    }
}
