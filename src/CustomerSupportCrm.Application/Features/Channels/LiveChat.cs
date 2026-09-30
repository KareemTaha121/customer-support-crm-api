using System.Security.Cryptography;
using System.Text;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Tickets;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Channels;

public sealed record StartChatRequest(string Name, string Email, string Message, string? Language);

public sealed record ChatStartedResponse(Guid ConversationId, string AccessToken, string TicketNumber);

public sealed record ChatMessageRequest(string Body);

public sealed record ChatConversationResponse(
    Guid Id,
    Guid TicketId,
    string TicketNumber,
    string VisitorName,
    string Status,
    Guid? AgentId,
    string? AgentName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastMessageAt);

public sealed record VisitorChatResponse(Guid Id, string Status, string? AgentName, IReadOnlyList<TicketMessageResponse> Messages);

internal static class ChatTokens
{
    public const string HeaderName = "X-Chat-Token";
    public const string NotFoundCode = "CHAT_NOT_FOUND";

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static async Task<ChatConversation> LoadForVisitorAsync(IApplicationDbContext db, Guid conversationId, string? token, CancellationToken cancellationToken)
    {
        var conversation = string.IsNullOrEmpty(token)
            ? null
            : await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (conversation is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(conversation.AccessTokenHash), Encoding.UTF8.GetBytes(Hash(token!))))
        {
            throw new NotFoundException(NotFoundCode, "The conversation was not found.");
        }

        return conversation;
    }
}

/// <summary>Lets SignalR visitors join only conversations whose token they hold.</summary>
internal sealed class ChatAccessValidator(IApplicationDbContext db) : IChatAccessValidator
{
    public async Task<bool> CanJoinAsync(Guid conversationId, string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            await ChatTokens.LoadForVisitorAsync(db, conversationId, accessToken, cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}

// ---------- Visitor side (anonymous) ----------

public sealed record StartChatCommand(StartChatRequest Request) : IRequest<ChatStartedResponse>;

internal sealed class StartChatValidator : AbstractValidator<StartChatCommand>
{
    public StartChatValidator()
    {
        RuleFor(c => c.Request.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.Request.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Request.Message).NotEmpty().MaximumLength(5000);
    }
}

internal sealed class StartChatHandler(
    IApplicationDbContext db,
    CustomerResolver customers,
    TicketFactory tickets,
    IRealtimeNotifier realtime,
    TimeProvider time)
    : IRequestHandler<StartChatCommand, ChatStartedResponse>
{
    public async Task<ChatStartedResponse> Handle(StartChatCommand request, CancellationToken cancellationToken)
    {
        var input = request.Request;
        var customer = await customers.ResolveAsync(ContactType.Email, input.Email, input.Name, null, cancellationToken);
        var ticket = await tickets.CreateAsync(
            new NewTicket(customer.Id, $"Chat: {input.Name.Trim()}", input.Message.Trim(), null, TicketPriority.Medium, TicketChannel.Chat, null, null, ["chat"], EmailAddress.Normalize(input.Email)),
            scope: null,
            cancellationToken);

        var token = ChatConversation.NewAccessToken();
        var conversation = ChatConversation.Start(ticket.Id, customer.Id, input.Name, ChatTokens.Hash(token), time.GetUtcNow());
        db.ChatConversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        await realtime.SendToGroupAsync(
            RealtimeGroups.ChatAgents,
            RealtimeEvents.ChatUpdated,
            new { conversationId = conversation.Id, status = conversation.Status.ToString(), visitorName = conversation.VisitorName, ticketNumber = ticket.Number },
            cancellationToken);

        return new ChatStartedResponse(conversation.Id, token, ticket.Number);
    }
}

public sealed record VisitorChatMessageCommand(Guid ConversationId, string? Token, string Body) : IRequest;

internal sealed class VisitorChatMessageValidator : AbstractValidator<VisitorChatMessageCommand>
{
    public VisitorChatMessageValidator() => RuleFor(c => c.Body).NotEmpty().MaximumLength(5000);
}

internal sealed class VisitorChatMessageHandler(IApplicationDbContext db, TicketMessageWriter writer, TimeProvider time) : IRequestHandler<VisitorChatMessageCommand>
{
    public async Task Handle(VisitorChatMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await ChatTokens.LoadForVisitorAsync(db, request.ConversationId, request.Token, cancellationToken);
        conversation.Touch(time.GetUtcNow());
        var ticket = await db.Tickets.SingleAsync(t => t.Id == conversation.TicketId, cancellationToken);

        await writer.AddAsync(ticket, MessageAuthorType.Customer, null, conversation.CustomerId, request.Body, false, TicketChannel.Chat, null, [], [], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record GetVisitorChatQuery(Guid ConversationId, string? Token) : IRequest<VisitorChatResponse>;

internal sealed class GetVisitorChatHandler(IApplicationDbContext db) : IRequestHandler<GetVisitorChatQuery, VisitorChatResponse>
{
    public async Task<VisitorChatResponse> Handle(GetVisitorChatQuery request, CancellationToken cancellationToken)
    {
        var conversation = await ChatTokens.LoadForVisitorAsync(db, request.ConversationId, request.Token, cancellationToken);
        var agent = await db.Users.Where(u => u.Id == conversation.AgentId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken);
        var messages = await TicketQueries.GetMessagesAsync(db, conversation.TicketId, publicOnly: true, _ => string.Empty, cancellationToken);
        return new VisitorChatResponse(conversation.Id, conversation.Status.ToString(), agent, messages);
    }
}

public sealed record CloseChatCommand(Guid ConversationId, string? VisitorToken) : IRequest;

/// <summary>Closing a chat resolves its ticket (reopened automatically if the customer writes again elsewhere).</summary>
internal sealed class CloseChatHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, IRealtimeNotifier realtime, TimeProvider time)
    : IRequestHandler<CloseChatCommand>
{
    public async Task Handle(CloseChatCommand request, CancellationToken cancellationToken)
    {
        ChatConversation conversation;
        if (currentUser.IsAuthenticated && request.VisitorToken is null)
        {
            conversation = await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken)
                ?? throw new NotFoundException(ChatTokens.NotFoundCode, "The conversation was not found.");
            await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), conversation.TicketId, cancellationToken);
        }
        else
        {
            conversation = await ChatTokens.LoadForVisitorAsync(db, request.ConversationId, request.VisitorToken, cancellationToken);
        }

        var now = time.GetUtcNow();
        conversation.Close(now);
        var ticket = await db.Tickets.SingleAsync(t => t.Id == conversation.TicketId, cancellationToken);
        if (ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            ticket.ChangeStatus(TicketStatus.Resolved, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await realtime.SendToGroupAsync(RealtimeGroups.ChatConversation(conversation.Id), RealtimeEvents.ChatUpdated, new { conversationId = conversation.Id, status = conversation.Status.ToString() }, cancellationToken);
        await realtime.SendToGroupAsync(RealtimeGroups.ChatAgents, RealtimeEvents.ChatUpdated, new { conversationId = conversation.Id, status = conversation.Status.ToString() }, cancellationToken);
    }
}

internal sealed class VisitorChatEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat/conversations").WithTags("Live chat").RequireRateLimiting(RateLimitPolicies.Public);

        group.MapPost("/", async (StartChatRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/public/chat/conversations", await sender.Send(new StartChatCommand(request), ct)))
            .WithName("StartChat")
            .Produces<ApiResponse<ChatStartedResponse>>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new GetVisitorChatQuery(id, http.Headers[ChatTokens.HeaderName]), ct)))
            .WithName("GetVisitorChat")
            .Produces<ApiResponse<VisitorChatResponse>>();

        group.MapPost("/{id:guid}/messages", async (Guid id, ChatMessageRequest request, HttpRequest http, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new VisitorChatMessageCommand(id, http.Headers[ChatTokens.HeaderName], request.Body), ct);
                return ApiResults.Success();
            })
            .WithName("SendVisitorChatMessage")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/{id:guid}/close", async (Guid id, HttpRequest http, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new CloseChatCommand(id, http.Headers[ChatTokens.HeaderName].ToString()), ct);
                return ApiResults.Success();
            })
            .WithName("CloseVisitorChat")
            .Produces<ApiResponse<object?>>();
    }
}

// ---------- Agent side ----------

/// <param name="Status">waiting, active, mine or closed.</param>
public sealed record ListChatsQuery(string? Status = null) : IRequest<IReadOnlyList<ChatConversationResponse>>;

internal sealed class ListChatsHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser) : IRequestHandler<ListChatsQuery, IReadOnlyList<ChatConversationResponse>>
{
    public async Task<IReadOnlyList<ChatConversationResponse>> Handle(ListChatsQuery request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var me = currentUser.UserId;
        var visibleTickets = db.Tickets.AsNoTracking().WhereInScope(scope).Select(t => t.Id);
        var query = db.ChatConversations.AsNoTracking().Where(c => visibleTickets.Contains(c.TicketId));

        query = request.Status switch
        {
            "active" => query.Where(c => c.Status == ChatStatus.Active),
            "mine" => query.Where(c => c.Status == ChatStatus.Active && c.AgentId == me),
            "closed" => query.Where(c => c.Status == ChatStatus.Closed),
            _ => query.Where(c => c.Status == ChatStatus.Waiting),
        };

        return await query.OrderBy(c => c.StartedAt).Take(100)
            .Select(c => new ChatConversationResponse(
                c.Id,
                c.TicketId,
                db.Tickets.Where(t => t.Id == c.TicketId).Select(t => t.Number).First(),
                c.VisitorName,
                c.Status.ToString(),
                c.AgentId == null ? null : c.AgentId.Value.Value,
                db.Users.Where(u => u.Id == c.AgentId).Select(u => u.DisplayName).FirstOrDefault(),
                c.StartedAt,
                c.LastMessageAt))
            .ToListAsync(cancellationToken);
    }
}

public sealed record AcceptChatCommand(Guid ConversationId) : IRequest;

internal sealed class AcceptChatHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, IRealtimeNotifier realtime, TimeProvider time) : IRequestHandler<AcceptChatCommand>
{
    public async Task Handle(AcceptChatCommand request, CancellationToken cancellationToken)
    {
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken)
            ?? throw new NotFoundException(ChatTokens.NotFoundCode, "The conversation was not found.");
        var ticket = await TicketQueries.LoadAsync(db, await scopes.GetAsync(cancellationToken), conversation.TicketId, cancellationToken);

        conversation.Accept(currentUser.UserId, time.GetUtcNow());
        ticket.AssignTo(currentUser.UserId);
        await db.SaveChangesAsync(cancellationToken);

        var agent = await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.DisplayName).FirstAsync(cancellationToken);
        var payload = new { conversationId = conversation.Id, status = conversation.Status.ToString(), agentName = agent };
        await realtime.SendToGroupAsync(RealtimeGroups.ChatConversation(conversation.Id), RealtimeEvents.ChatUpdated, payload, cancellationToken);
        await realtime.SendToGroupAsync(RealtimeGroups.ChatAgents, RealtimeEvents.ChatUpdated, payload, cancellationToken);
    }
}

/// <summary>
/// Staff transcript of one conversation (public messages of the linked ticket). Needs only
/// <c>chat.handle</c> plus access to the ticket's branch/department, not <c>tickets.view</c>.
/// </summary>
public sealed record GetAgentChatMessagesQuery(Guid ConversationId) : IRequest<IReadOnlyList<TicketMessageResponse>>;

internal sealed class GetAgentChatMessagesHandler(IApplicationDbContext db, IAccessScopeProvider scopes)
    : IRequestHandler<GetAgentChatMessagesQuery, IReadOnlyList<TicketMessageResponse>>
{
    public async Task<IReadOnlyList<TicketMessageResponse>> Handle(GetAgentChatMessagesQuery request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var visibleTickets = db.Tickets.AsNoTracking().WhereInScope(scope).Select(t => t.Id);
        var ticketId = await db.ChatConversations.AsNoTracking()
            .Where(c => c.Id == request.ConversationId && visibleTickets.Contains(c.TicketId))
            .Select(c => (Guid?)c.TicketId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(ChatTokens.NotFoundCode, "The conversation was not found.");

        return await TicketQueries.GetMessagesAsync(db, ticketId, publicOnly: true, _ => string.Empty, cancellationToken);
    }
}

public sealed record AgentChatMessageCommand(Guid ConversationId, string Body) : IRequest;

internal sealed class AgentChatMessageValidator : AbstractValidator<AgentChatMessageCommand>
{
    public AgentChatMessageValidator() => RuleFor(c => c.Body).NotEmpty().MaximumLength(5000);
}

internal sealed class AgentChatMessageHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser, TicketMessageWriter writer, TimeProvider time)
    : IRequestHandler<AgentChatMessageCommand>
{
    public async Task Handle(AgentChatMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken)
            ?? throw new NotFoundException(ChatTokens.NotFoundCode, "The conversation was not found.");
        var ticket = await TicketQueries.LoadAsync(db, await scopes.GetAsync(cancellationToken), conversation.TicketId, cancellationToken);

        if (conversation.Status == ChatStatus.Waiting)
        {
            conversation.Accept(currentUser.UserId, time.GetUtcNow());
            ticket.AssignTo(currentUser.UserId);
        }

        conversation.Touch(time.GetUtcNow());
        await writer.AddAsync(ticket, MessageAuthorType.Agent, currentUser.UserId, null, request.Body, false, TicketChannel.Chat, null, [], [], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class AgentChatEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat/conversations").WithTags("Live chat").RequireAuthorization(Permissions.ChatHandle);

        group.MapGet("/", async (string? status, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListChatsQuery(status), ct)))
            .WithName("ListChats")
            .Produces<ApiResponse<IReadOnlyList<ChatConversationResponse>>>();

        group.MapGet("/{id:guid}/messages", async (Guid id, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new GetAgentChatMessagesQuery(id), ct)))
            .WithName("GetAgentChatMessages")
            .Produces<ApiResponse<IReadOnlyList<TicketMessageResponse>>>();

        group.MapPost("/{id:guid}/accept", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new AcceptChatCommand(id), ct);
                return ApiResults.Success();
            })
            .WithName("AcceptChat")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/{id:guid}/messages", async (Guid id, ChatMessageRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new AgentChatMessageCommand(id, request.Body), ct);
                return ApiResults.Success();
            })
            .WithName("SendAgentChatMessage")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/{id:guid}/close", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new CloseChatCommand(id, null), ct);
                return ApiResults.Success();
            })
            .WithName("CloseChat")
            .Produces<ApiResponse<object?>>();
    }
}
