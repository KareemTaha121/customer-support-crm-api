namespace CustomerSupportCrm.Contracts.Ai;

/// <param name="Sentiment">positive, neutral, negative or frustrated.</param>
public sealed record TicketSummaryResponse(Guid SuggestionId, string Summary, IReadOnlyList<string> KeyPoints, string Sentiment, string? NextStep);

/// <param name="Tone">friendly (default), formal or empathetic.</param>
/// <param name="Instructions">Optional guidance from the agent, e.g. "offer a refund".</param>
public sealed record SuggestReplyRequest(string? Tone, string? Instructions);

public sealed record SuggestedReplyResponse(Guid SuggestionId, string Reply, string Language, IReadOnlyList<Guid> UsedArticleIds);

public sealed record CategorizationResponse(
    Guid SuggestionId,
    Guid? CategoryId,
    string? CategoryName,
    string Priority,
    IReadOnlyList<string> Tags,
    double Confidence,
    string Reasoning);

public sealed record SolutionSuggestion(Guid ArticleId, string Title, string Slug, string Reason);

public sealed record SolutionSuggestionsResponse(Guid SuggestionId, IReadOnlyList<SolutionSuggestion> Solutions);

public sealed record AiFeedbackRequest(bool Accepted);

public sealed record ChatbotTurn(string Role, string Content);

public sealed record ChatbotRequest(IReadOnlyList<ChatbotTurn> Messages, string? Language);

/// <param name="Handoff">True when the assistant could not answer from the knowledge base and suggests a human.</param>
public sealed record ChatbotResponse(string Answer, bool Handoff, IReadOnlyList<ChatbotSource> Sources);

public sealed record ChatbotSource(Guid ArticleId, string Title, string Slug);

public sealed record AiStatusResponse(bool Enabled);
