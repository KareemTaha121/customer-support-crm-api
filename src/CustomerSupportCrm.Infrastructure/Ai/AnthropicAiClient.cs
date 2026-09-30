using Anthropic;
using Anthropic.Models.Beta.Messages;
using CustomerSupportCrm.Application.Abstractions.Ai;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; init; }

    /// <summary>Anthropic API key. When empty, the SDK's standard credential resolution applies (ANTHROPIC_API_KEY, ...).</summary>
    public string? ApiKey { get; init; }

    public string Model { get; init; } = "claude-opus-5-5";

    /// <summary>Re-serve safety-policy refusals with a fallback model chosen by the API (server-side fallback beta).</summary>
    public bool UseRefusalFallback { get; init; } = true;
}

/// <summary>
/// Claude via the official Anthropic SDK: adaptive thinking (the model's default), effort per
/// task, structured JSON output, refusal detection and server-side refusal fallback.
/// </summary>
internal sealed class AnthropicAiClient : IAiCompletionClient, IDisposable
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AiOptions _options;
    private readonly AnthropicClient? _client;

    public AnthropicAiClient(IOptions<AiOptions> options)
    {
        _options = options.Value;
        var hasKey = !string.IsNullOrWhiteSpace(_options.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        if (_options.Enabled && hasKey)
        {
            _client = string.IsNullOrWhiteSpace(_options.ApiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = _options.ApiKey };
        }
    }

    public bool IsConfigured => _client is not null;

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_client is null)
        {
            throw new InvalidOperationException("The AI client is not configured.");
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxTokens,
            System = request.System,
            Messages = [.. request.Messages.Select(m => new BetaMessageParam { Role = m.Role == AiRole.User ? Role.User : Role.Assistant, Content = m.Content })],
            OutputConfig = new BetaOutputConfig
            {
                Effort = request.Effort switch
                {
                    "low" => Effort.Low,
                    "high" => Effort.High,
                    _ => Effort.Medium,
                },
                Format = request.JsonSchema is { } schema ? new BetaJsonOutputFormat { Schema = schema.ToDictionary(p => p.Key, p => p.Value) } : null,
            },
            Betas = _options.UseRefusalFallback ? [FallbackBeta] : null,
            Fallbacks = _options.UseRefusalFallback ? (BetaFallbacksParam)new Default() : null,
        };

        var response = await _client.Beta.Messages.Create(parameters, cancellationToken);

        if (response.StopReason == "refusal")
        {
            return new AiCompletion(string.Empty, true, response.StopDetails?.Category?.ToString(), response.Model.ToString(), response.Usage.InputTokens, response.Usage.OutputTokens);
        }

        var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : string.Empty));
        return new AiCompletion(text, false, null, response.Model.ToString(), response.Usage.InputTokens, response.Usage.OutputTokens);
    }

    public void Dispose() => (_client as IDisposable)?.Dispose();
}
