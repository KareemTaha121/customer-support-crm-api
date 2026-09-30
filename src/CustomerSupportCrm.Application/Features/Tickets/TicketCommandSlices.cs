using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Customers;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Tickets;

// ---------- Create ----------

public sealed record CreateTicketCommand(
    Guid CustomerId,
    string Subject,
    string? Description,
    Guid? CategoryId,
    string Priority,
    string? Channel,
    Guid? BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string> Tags,
    Guid? AssignedAgentId) : IRequest<TicketResponse>;

internal sealed class CreateTicketValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(c => c.Description).MaximumLength(Ticket.DescriptionMaxLength);
        RuleFor(c => c.Priority).NotEmpty().IsEnumName(typeof(TicketPriority), caseSensitive: false);
        RuleFor(c => c.Channel).Must(c => c is null or nameof(TicketChannel.Agent) or nameof(TicketChannel.Phone)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Tags).NotNull().Must(t => t.Count <= Ticket.MaxTags);
    }
}

internal sealed class CreateTicketHandler(
    IApplicationDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    TicketFactory factory)
    : IRequestHandler<CreateTicketCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        await CustomerQueries.EnsureAccessibleAsync(db, scope, request.CustomerId, cancellationToken);

        var ticket = await factory.CreateAsync(
            new NewTicket(
                request.CustomerId,
                request.Subject,
                request.Description ?? string.Empty,
                request.CategoryId,
                Enum.Parse<TicketPriority>(request.Priority, ignoreCase: true),
                request.Channel is null ? TicketChannel.Agent : Enum.Parse<TicketChannel>(request.Channel),
                request.BranchId,
                request.DepartmentId,
                request.Tags,
                ReplyAddress: null),
            scope,
            cancellationToken);

        if (request.AssignedAgentId is { } agent)
        {
            await TicketAssignment.AssignAsync(db, currentUser, ticket, new UserId(agent), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await TicketQueries.GetResponseAsync(db, AccessScope.Everything, ticket.Id, cancellationToken);
    }
}

public sealed record NewTicket(
    Guid CustomerId,
    string Subject,
    string Description,
    Guid? CategoryId,
    TicketPriority Priority,
    TicketChannel Channel,
    Guid? BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string> Tags,
    string? ReplyAddress);

/// <summary>
/// Creates tickets for every channel (agent, portal, web form, email, WhatsApp, SMS, chat, API):
/// numbering, category defaults and ownership. SLA and assignment rules run on TicketCreated.
/// </summary>
public sealed class TicketFactory(IApplicationDbContext db, ISequenceGenerator sequences, TimeProvider time)
{
    /// <param name="scope">Caller scope for staff-created tickets; null for customer/system channels.</param>
    public async Task<Ticket> CreateAsync(NewTicket request, AccessScope? scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == request.CustomerId)
            .Select(c => new { c.BranchId, c.DepartmentId, c.PrimaryEmail, c.PrimaryPhone })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw CustomerQueries.NotFound();

        var priority = request.Priority;
        Guid? departmentId = request.DepartmentId;
        Guid branchId = request.BranchId ?? customer.BranchId;

        if (request.CategoryId is { } categoryId)
        {
            var category = await db.TicketCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId && c.IsActive, cancellationToken)
                ?? throw new NotFoundException(TicketErrors.CategoryNotFound, "The category was not found.");

            if (departmentId is null && category.DefaultDepartmentId is { } defaultDepartment)
            {
                departmentId = defaultDepartment;
                branchId = await db.Departments.Where(d => d.Id == defaultDepartment).Select(d => d.BranchId).SingleAsync(cancellationToken);
            }
        }

        if (request.BranchId is null && request.DepartmentId is null && departmentId is null)
        {
            departmentId = customer.DepartmentId;
        }

        scope?.EnsureCanAssign(branchId, departmentId);
        await OrganizationUnits.EnsureValidAsync(db, branchId, departmentId, cancellationToken);

        var number = Ticket.FormatNumber(await sequences.NextValueAsync(Sequences.TicketNumbers, cancellationToken));
        var ticket = Ticket.Create(
            number,
            request.Subject,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priority,
            request.Channel,
            request.ReplyAddress ?? customer.PrimaryEmail,
            branchId,
            departmentId,
            request.Tags,
            time.GetUtcNow());

        db.Tickets.Add(ticket);
        return ticket;
    }
}

// ---------- Update ----------

public sealed record UpdateTicketCommand(Guid TicketId, string Subject, string? Description, Guid? CategoryId, string Priority, IReadOnlyList<string> Tags)
    : IRequest<TicketResponse>;

internal sealed class UpdateTicketValidator : AbstractValidator<UpdateTicketCommand>
{
    public UpdateTicketValidator()
    {
        RuleFor(c => c.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(c => c.Description).MaximumLength(Ticket.DescriptionMaxLength);
        RuleFor(c => c.Priority).NotEmpty().IsEnumName(typeof(TicketPriority), caseSensitive: false);
        RuleFor(c => c.Tags).NotNull().Must(t => t.Count <= Ticket.MaxTags);
    }
}

internal sealed class UpdateTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<UpdateTicketCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(UpdateTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);

        if (request.CategoryId is { } categoryId && !await db.TicketCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new NotFoundException(TicketErrors.CategoryNotFound, "The category was not found.");
        }

        ticket.Edit(request.Subject, request.Description ?? string.Empty, request.Tags);
        ticket.Categorize(request.CategoryId);
        ticket.ChangePriority(Enum.Parse<TicketPriority>(request.Priority, ignoreCase: true));
        await db.SaveChangesAsync(cancellationToken);

        return await TicketQueries.GetResponseAsync(db, scope, ticket.Id, cancellationToken);
    }
}

// ---------- Assign / transfer ----------

internal static class TicketAssignment
{
    /// <summary>Self-assignment needs tickets.update; assigning others or unassigning needs tickets.assign.</summary>
    public static async Task AssignAsync(IApplicationDbContext db, ICurrentUser currentUser, Ticket ticket, UserId? agentId, CancellationToken cancellationToken)
    {
        var self = agentId is { } id && id == currentUser.UserId;
        if (!self && !currentUser.HasPermission(Permissions.TicketsAssign))
        {
            throw new ForbiddenException(TicketErrors.AssignForbidden, "You can only assign tickets to yourself.");
        }

        if (agentId is { } agent && !await TicketQueries.CanHandleAsync(db, agent, ticket.BranchId, ticket.DepartmentId, cancellationToken))
        {
            throw new ConflictException(TicketErrors.AgentNotEligible, "This agent is inactive or has no access to the ticket's branch/department.");
        }

        ticket.AssignTo(agentId);
    }
}

public sealed record AssignTicketCommand(Guid TicketId, Guid? AgentId) : IRequest<TicketResponse>;

internal sealed class AssignTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser) : IRequestHandler<AssignTicketCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(AssignTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);
        await TicketAssignment.AssignAsync(db, currentUser, ticket, request.AgentId is { } id ? new UserId(id) : null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await TicketQueries.GetResponseAsync(db, scope, ticket.Id, cancellationToken);
    }
}

public sealed record TransferTicketCommand(Guid TicketId, Guid BranchId, Guid? DepartmentId) : IRequest<TicketResponse>;

/// <summary>Moves the ticket to another branch/department; the assignee is cleared when no longer eligible.</summary>
internal sealed class TransferTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<TransferTicketCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(TransferTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);
        await OrganizationUnits.EnsureValidAsync(db, request.BranchId, request.DepartmentId, cancellationToken);

        ticket.TransferTo(request.BranchId, request.DepartmentId);
        if (ticket.AssignedAgentId is { } agent && !await TicketQueries.CanHandleAsync(db, agent, request.BranchId, request.DepartmentId, cancellationToken))
        {
            ticket.AssignTo(null);
        }

        await db.SaveChangesAsync(cancellationToken);

        // The caller may lose access by transferring out of their own scope.
        return await TicketQueries.GetResponseAsync(db, AccessScope.Everything, ticket.Id, cancellationToken);
    }
}

// ---------- Status / escalation ----------

/// <param name="Status">A target status, or "Reopen".</param>
public sealed record ChangeTicketStatusCommand(Guid TicketId, string Status) : IRequest<TicketResponse>;

internal sealed class ChangeTicketStatusValidator : AbstractValidator<ChangeTicketStatusCommand>
{
    public ChangeTicketStatusValidator() =>
        RuleFor(c => c.Status).NotEmpty().Must(s => s == "Reopen" || Enum.TryParse<TicketStatus>(s, out _)).WithErrorCode(ErrorCodes.InvalidValue);
}

internal sealed class ChangeTicketStatusHandler(IApplicationDbContext db, IAccessScopeProvider scopes, TimeProvider time) : IRequestHandler<ChangeTicketStatusCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(ChangeTicketStatusCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);

        if (request.Status == "Reopen")
        {
            ticket.Reopen(time.GetUtcNow());
        }
        else
        {
            ticket.ChangeStatus(Enum.Parse<TicketStatus>(request.Status), time.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return await TicketQueries.GetResponseAsync(db, scope, ticket.Id, cancellationToken);
    }
}

public sealed record EscalateTicketCommand(Guid TicketId, string Reason) : IRequest<TicketResponse>;

internal sealed class EscalateTicketValidator : AbstractValidator<EscalateTicketCommand>
{
    public EscalateTicketValidator() => RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class EscalateTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes, TimeProvider time) : IRequestHandler<EscalateTicketCommand, TicketResponse>
{
    public async Task<TicketResponse> Handle(EscalateTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);
        ticket.Escalate(request.Reason, time.GetUtcNow(), automatic: false);
        await db.SaveChangesAsync(cancellationToken);
        return await TicketQueries.GetResponseAsync(db, scope, ticket.Id, cancellationToken);
    }
}

public sealed record DeleteTicketCommand(Guid TicketId) : IRequest;

internal sealed class DeleteTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IAuditTrail audit) : IRequestHandler<DeleteTicketCommand>
{
    public async Task Handle(DeleteTicketCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);
        db.Tickets.Remove(ticket);
        audit.Record("tickets.deleted", "Ticket", ticket.Id.ToString(), oldValues: new { ticket.Number, ticket.Subject, ticket.CustomerId });
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Endpoints ----------

internal sealed class TicketCommandEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets").WithTags("Tickets");

        group.MapPost("/", async (CreateTicketRequest request, ISender sender, CancellationToken ct) =>
            {
                var ticket = await sender.Send(
                    new CreateTicketCommand(request.CustomerId, request.Subject, request.Description, request.CategoryId, request.Priority, request.Channel, request.BranchId, request.DepartmentId, request.Tags ?? [], request.AssignedAgentId),
                    ct);
                return ApiResults.Created($"/api/v1/tickets/{ticket.Id}", ticket);
            })
            .RequireAuthorization(Permissions.TicketsCreate)
            .WithName("CreateTicket")
            .Produces<ApiResponse<TicketResponse>>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTicketRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new UpdateTicketCommand(id, request.Subject, request.Description, request.CategoryId, request.Priority, request.Tags ?? []), ct)))
            .RequireAuthorization(Permissions.TicketsUpdate)
            .WithName("UpdateTicket")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignTicketRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new AssignTicketCommand(id, request.AgentId), ct)))
            .RequireAuthorization(Permissions.TicketsUpdate)
            .WithName("AssignTicket")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapPost("/{id:guid}/transfer", async (Guid id, TransferTicketRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new TransferTicketCommand(id, request.BranchId, request.DepartmentId), ct)))
            .RequireAuthorization(Permissions.TicketsAssign)
            .WithName("TransferTicket")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapPost("/{id:guid}/status", async (Guid id, ChangeTicketStatusRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new ChangeTicketStatusCommand(id, request.Status), ct)))
            .RequireAuthorization(Permissions.TicketsUpdate)
            .WithName("ChangeTicketStatus")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapPost("/{id:guid}/escalate", async (Guid id, EscalateTicketRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new EscalateTicketCommand(id, request.Reason), ct)))
            .RequireAuthorization(Permissions.TicketsEscalate)
            .WithName("EscalateTicket")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteTicketCommand(id), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.TicketsDelete)
            .WithName("DeleteTicket")
            .Produces<ApiResponse<object?>>();
    }
}
