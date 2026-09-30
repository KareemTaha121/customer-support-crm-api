namespace CustomerSupportCrm.Application.Abstractions.Integrations;

/// <summary>Encrypts secrets that must be recoverable (e.g. webhook signing secrets).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

public sealed record WebhookSendResult(bool Succeeded, int? StatusCode, string? Error);

/// <summary>
/// Posts a signed webhook payload. Implementations must refuse private, loopback and link-local
/// destinations (SSRF protection).
/// </summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(string url, string eventType, Guid deliveryId, string payload, string secret, CancellationToken cancellationToken);
}

/// <summary>The external system calling the /api/v1/external API.</summary>
public interface ICurrentApiClient
{
    bool IsAuthenticated { get; }

    Guid ApiKeyId { get; }

    string Name { get; }
}
