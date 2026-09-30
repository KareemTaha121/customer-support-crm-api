using CustomerSupportCrm.Application.Abstractions.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CustomerSupportCrm.Infrastructure.Realtime;

/// <summary>
/// Visitor/customer side of live chat. Anonymous, but a connection only receives a
/// conversation's messages after proving its per-conversation access token.
/// </summary>
[AllowAnonymous]
public sealed class ChatHub(IChatAccessValidator access) : Hub
{
    public const string Path = "/hubs/chat";

    public async Task JoinConversation(Guid conversationId, string accessToken)
    {
        if (!await access.CanJoinAsync(conversationId, accessToken, Context.ConnectionAborted))
        {
            throw new HubException("Conversation not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.ChatConversation(conversationId));
    }
}
