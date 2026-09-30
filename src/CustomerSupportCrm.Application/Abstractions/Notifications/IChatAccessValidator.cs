namespace CustomerSupportCrm.Application.Abstractions.Notifications;

/// <summary>Checks that a visitor holds the access token of the chat conversation they join.</summary>
public interface IChatAccessValidator
{
    Task<bool> CanJoinAsync(Guid conversationId, string accessToken, CancellationToken cancellationToken);
}
