using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Application.Abstractions.Notifications;

/// <summary>Decides who may follow a chat conversation over SignalR.</summary>
public interface IChatAccessValidator
{
    /// <summary>A visitor must hold the conversation's access token.</summary>
    Task<bool> CanJoinAsync(Guid conversationId, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// A staff user (already checked for <c>chat.handle</c>) may follow only conversations whose
    /// ticket is in their branch/department scope, the same rule as the staff chat HTTP routes.
    /// </summary>
    Task<bool> CanStaffJoinAsync(Guid conversationId, UserId userId, bool allBranches, CancellationToken cancellationToken);
}
