using System.Text.Json;

namespace CustomerSupportCrm.Application.Abstractions.Ai;

public enum AiRole
{
    User,
    Assistant,
}

public sealed record AiMessage(AiRole Role, string Content);

/// <param name="JsonSchema">When set, the response is constrained to this JSON schema (structured output).</param>
/// <param name="Effort">low, medium or high: depth of reasoning versus cost and latency.</param>
public sealed record AiRequest(
    string Feature,
    string System,
    IReadOnlyList<AiMessage> Messages,
    IReadOnlyDictionary<string, JsonElement>? JsonSchema,
    int MaxTokens,
    string Effort);

/// <param name="Refused">The provider declined (safety policy); <see cref="Text"/> is empty.</param>
public sealed record AiCompletion(string Text, bool Refused, string? RefusalCategory, string Model, long InputTokens, long OutputTokens);

/// <summary>
/// Provider-neutral LLM access. The domain never sees provider SDKs; output is untrusted text
/// that agents review before anything is sent or applied.
/// </summary>
public interface IAiCompletionClient
{
    bool IsConfigured { get; }

    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}
