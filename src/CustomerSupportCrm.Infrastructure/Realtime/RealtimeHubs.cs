using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCrm.Infrastructure.Realtime;

/// <summary>
/// Staff connection: per-user notifications (SignalR user = JWT "sub") and, for chat agents,
/// the live-chat queue. Clients only listen; all actions go through the HTTP API.
/// </summary>
[Authorize(Policy = PolicyNames.Staff)]
public sealed class StaffHub(IChatAccessValidator access) : Hub
{
    public const string Path = "/hubs/staff";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.HasClaim(CrmClaimTypes.Permission, Permissions.ChatHandle) == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.ChatAgents);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Lets a chat agent follow one conversation (e.g. while viewing it). Requires <c>chat.handle</c>
    /// and the conversation's ticket in the agent's branch/department scope; otherwise the
    /// conversation is reported as not found so its existence is not disclosed.
    /// </summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var user = Context.User;
        if (user?.HasClaim(CrmClaimTypes.Permission, Permissions.ChatHandle) != true
            || !Guid.TryParse(user.FindFirst(CrmClaimTypes.Subject)?.Value, out var userId)
            || !await access.CanStaffJoinAsync(
                conversationId,
                new UserId(userId),
                user.HasClaim(CrmClaimTypes.Permission, Permissions.DataAllBranches),
                Context.ConnectionAborted))
        {
            throw new HubException("Conversation not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.ChatConversation(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.ChatConversation(conversationId));
}

internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(CrmClaimTypes.Subject)?.Value;
}

internal sealed partial class SignalRRealtimeNotifier(
    IHubContext<StaffHub> staffHub,
    IHubContext<ChatHub> chatHub,
    ILogger<SignalRRealtimeNotifier> logger)
    : IRealtimeNotifier
{
    public async Task SendToUserAsync(UserId userId, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await staffHub.Clients.User(userId.Value.ToString()).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPushFailed(logger, eventName, exception);
        }
    }

    public async Task SendToGroupAsync(string group, string eventName, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await staffHub.Clients.Group(group).SendAsync(eventName, payload, cancellationToken);
            await chatHub.Clients.Group(group).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPushFailed(logger, eventName, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Realtime push of {EventName} failed")]
    private static partial void LogPushFailed(ILogger logger, string eventName, Exception exception);
}
