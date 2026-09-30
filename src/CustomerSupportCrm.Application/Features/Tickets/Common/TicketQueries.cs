using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Tickets.Common;

public static class TicketErrors
{
    public const string TicketNotFound = "TICKET_NOT_FOUND";
    public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
    public const string AgentNotEligible = "AGENT_NOT_ELIGIBLE";
    public const string AssignForbidden = "ASSIGN_FORBIDDEN";
    public const string CategoryInUse = "CATEGORY_IN_USE";
}

internal static class TicketQueries
{
    public static NotFoundException NotFound() => new(TicketErrors.TicketNotFound, "The ticket was not found.");

    /// <summary>A tracked ticket the caller may access (tracked entities from this unit of work first).</summary>
    public static async Task<Ticket> LoadAsync(IApplicationDbContext db, AccessScope scope, Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken) ?? throw NotFound();
        scope.EnsureAccess(ticket, TicketErrors.TicketNotFound, "The ticket was not found.");
        return ticket;
    }

    /// <summary>A tracked ticket regardless of scope, for event handlers and background jobs.</summary>
    public static async Task<Ticket?> FindTrackedAsync(IApplicationDbContext db, Guid ticketId, CancellationToken cancellationToken) =>
        db.Tickets.Local.FirstOrDefault(t => t.Id == ticketId)
        ?? await db.Tickets.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

    public static async Task EnsureAccessibleAsync(IApplicationDbContext db, AccessScope scope, Guid ticketId, CancellationToken cancellationToken)
    {
        if (!await db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).WhereInScope(scope).AnyAsync(cancellationToken))
        {
            throw NotFound();
        }
    }

    public static string SlaState(bool active, bool firstBreached, bool resolutionBreached, DateTimeOffset? firstWarned, DateTimeOffset? resolutionWarned, DateTimeOffset? firstDue, DateTimeOffset? resolutionDue) =>
        firstBreached || resolutionBreached ? "breached"
        : !active ? "none"
        : firstWarned is not null || resolutionWarned is not null ? "warning"
        : firstDue is not null || resolutionDue is not null ? "ok"
        : "none";

    public static async Task<List<TicketListItemResponse>> ProjectListAsync(IQueryable<Ticket> tickets, IApplicationDbContext db, CancellationToken cancellationToken)
    {
        var rows = await tickets
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Subject,
                t.Status,
                t.Priority,
                t.Channel,
                t.CustomerId,
                CustomerName = db.Customers.IgnoreQueryFilters().Where(c => c.Id == t.CustomerId).Select(c => c.Name).FirstOrDefault(),
                t.CategoryId,
                CategoryName = db.TicketCategories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.AssignedAgentId,
                AssignedAgentName = db.Users.Where(u => u.Id == t.AssignedAgentId).Select(u => u.DisplayName).FirstOrDefault(),
                t.BranchId,
                t.DepartmentId,
                DepartmentName = db.Departments.Where(d => d.Id == t.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                t.FirstResponseBreached,
                t.ResolutionBreached,
                t.FirstResponseWarnedAt,
                t.ResolutionWarnedAt,
                t.FirstResponseDueAt,
                t.ResolutionDueAt,
                t.FirstRespondedAt,
                t.EscalationLevel,
                t.CreatedAt,
                t.UpdatedAt,
                t.LastCustomerMessageAt,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(t => new TicketListItemResponse(
                t.Id,
                t.Number,
                t.Subject,
                t.Status.ToString(),
                t.Priority.ToString(),
                t.Channel.ToString(),
                t.CustomerId,
                t.CustomerName ?? string.Empty,
                t.CategoryId,
                t.CategoryName,
                t.AssignedAgentId?.Value,
                t.AssignedAgentName,
                t.BranchId,
                t.DepartmentId,
                t.DepartmentName,
                SlaState(t.Status.IsActive(), t.FirstResponseBreached, t.ResolutionBreached, t.FirstResponseWarnedAt, t.ResolutionWarnedAt, t.FirstRespondedAt is null ? t.FirstResponseDueAt : null, t.ResolutionDueAt),
                t.FirstRespondedAt is null ? t.FirstResponseDueAt : null,
                t.ResolutionDueAt,
                t.EscalationLevel,
                t.CreatedAt,
                t.UpdatedAt,
                t.LastCustomerMessageAt)),
        ];
    }

    public static async Task<TicketResponse> GetResponseAsync(IApplicationDbContext db, AccessScope scope, Guid ticketId, CancellationToken cancellationToken)
    {
        var t = await db.Tickets.AsNoTracking().Where(x => x.Id == ticketId).WhereInScope(scope).SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == t.CustomerId)
            .Select(c => new TicketCustomerResponse(c.Id, c.Number, c.Name, c.PrimaryEmail, c.PrimaryPhone, c.PreferredLanguage))
            .SingleAsync(cancellationToken);
        var names = await db.Tickets.AsNoTracking()
            .Where(x => x.Id == ticketId)
            .Select(x => new
            {
                Category = db.TicketCategories.Where(c => c.Id == x.CategoryId).Select(c => c.Name).FirstOrDefault(),
                Agent = db.Users.Where(u => u.Id == x.AssignedAgentId).Select(u => u.DisplayName).FirstOrDefault(),
                Branch = db.Branches.Where(b => b.Id == x.BranchId).Select(b => b.Name).FirstOrDefault(),
                Department = db.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                Policy = db.SlaPolicies.Where(p => p.Id == x.SlaPolicyId).Select(p => p.Name).FirstOrDefault(),
            })
            .SingleAsync(cancellationToken);

        var firstDue = t.FirstRespondedAt is null ? t.FirstResponseDueAt : null;
        var state = SlaState(t.IsActive, t.FirstResponseBreached, t.ResolutionBreached, t.FirstResponseWarnedAt, t.ResolutionWarnedAt, firstDue, t.ResolutionDueAt);
        var allowed = Ticket.AllowedTransitions(t.Status).Select(s => s.ToString()).ToList();
        if (t.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            allowed.Add("Reopen");
        }

        return new TicketResponse(
            t.Id,
            t.Number,
            t.Subject,
            t.Description,
            t.Status.ToString(),
            allowed,
            t.Priority.ToString(),
            t.Channel.ToString(),
            t.ReplyAddress,
            customer,
            t.CategoryId,
            names.Category,
            t.AssignedAgentId?.Value,
            names.Agent,
            t.BranchId,
            names.Branch ?? string.Empty,
            t.DepartmentId,
            names.Department,
            t.Tags,
            new TicketSlaResponse(t.SlaPolicyId, names.Policy, t.FirstResponseDueAt, t.FirstRespondedAt, t.FirstResponseBreached, t.ResolutionDueAt, t.ResolvedAt, t.ResolutionBreached, state),
            t.EscalationLevel,
            t.EscalatedAt,
            t.SatisfactionRating,
            t.SatisfactionComment,
            t.ClosedAt,
            t.CreatedAt,
            t.UpdatedAt);
    }

    /// <param name="publicOnly">Customer-facing view: no internal notes, no internal files.</param>
    public static async Task<IReadOnlyList<TicketMessageResponse>> GetMessagesAsync(IApplicationDbContext db, Guid ticketId, bool publicOnly, Func<Guid, string> downloadUrl, CancellationToken cancellationToken)
    {
        var messages = await db.TicketMessages.AsNoTracking()
            .Where(m => m.TicketId == ticketId && (!publicOnly || !m.IsInternal))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.AuthorType,
                m.AuthorUserId,
                m.AuthorCustomerId,
                AgentName = db.Users.Where(u => u.Id == m.AuthorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                CustomerName = db.Customers.IgnoreQueryFilters().Where(c => c.Id == m.AuthorCustomerId).Select(c => c.Name).FirstOrDefault(),
                m.Body,
                m.IsInternal,
                m.Channel,
                m.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var messageIds = messages.Select(m => (Guid?)m.Id).ToList();
        var files = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == AttachmentOwnerTypes.Ticket && a.OwnerId == ticketId && messageIds.Contains(a.ParentId) && (!publicOnly || a.IsPublic))
            .ToListAsync(cancellationToken);

        return
        [
            .. messages.Select(m => new TicketMessageResponse(
                m.Id,
                m.AuthorType.ToString(),
                m.AuthorUserId?.Value,
                m.AuthorCustomerId,
                m.AuthorType switch
                {
                    MessageAuthorType.Agent => m.AgentName ?? "Agent",
                    MessageAuthorType.Customer => m.CustomerName ?? "Customer",
                    _ => "System",
                },
                m.Body,
                m.IsInternal,
                m.Channel.ToString(),
                m.CreatedAt,
                [.. files.Where(f => f.ParentId == m.Id).Select(f => new AttachmentResponse(f.Id, f.FileName, f.ContentType, f.Size, f.IsPublic, null, f.CreatedAt, downloadUrl(f.Id)))])),
        ];
    }

    /// <summary>
    /// An agent may handle a ticket when active and either holds data.all_branches or has a scope
    /// covering the ticket's branch/department.
    /// </summary>
    public static async Task<bool> CanHandleAsync(IApplicationDbContext db, UserId agentId, Guid branchId, Guid? departmentId, CancellationToken cancellationToken) =>
        await db.Users.AnyAsync(
            u => u.Id == agentId
                && u.Status == UserStatus.Active
                && (db.Roles.Any(r => u.Roles.Any(m => m.RoleId == r.Id) && r.Permissions.Any(p => p.Permission == Permissions.DataAllBranches))
                    || u.Scopes.Any(s => (s.BranchId == branchId && s.DepartmentId == null) || (departmentId != null && s.DepartmentId == departmentId))),
            cancellationToken);

    /// <summary>Active agents (tickets.update) able to handle tickets in a unit, for routing and notifications.</summary>
    public static IQueryable<User> EligibleAgents(IApplicationDbContext db, Guid branchId, Guid? departmentId, string permission) =>
        db.Users.Where(u => u.Status == UserStatus.Active
            && db.Roles.Any(r => u.Roles.Any(m => m.RoleId == r.Id) && r.Permissions.Any(p => p.Permission == permission))
            && (db.Roles.Any(r => u.Roles.Any(m => m.RoleId == r.Id) && r.Permissions.Any(p => p.Permission == Permissions.DataAllBranches))
                || u.Scopes.Any(s => (s.BranchId == branchId && s.DepartmentId == null) || (departmentId != null && s.DepartmentId == departmentId))));

    public static string TicketLink(Guid ticketId) => $"/tickets/{ticketId}";

    public static string DownloadPath(Guid ticketId, Guid attachmentId) => $"/api/v1/tickets/{ticketId}/attachments/{attachmentId}";
}
