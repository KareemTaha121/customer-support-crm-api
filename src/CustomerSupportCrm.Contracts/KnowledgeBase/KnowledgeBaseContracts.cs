namespace CustomerSupportCrm.Contracts.KnowledgeBase;

public sealed record KnowledgeCategoryRequest(string Name, string? NameAr, string? Description, Guid? ParentId, int SortOrder, bool IsPublic);

public sealed record KnowledgeCategoryResponse(Guid Id, string Name, string? NameAr, string? Description, Guid? ParentId, int SortOrder, bool IsPublic, int ArticleCount);

/// <param name="Type">Faq, Article, Guide or Solution.</param>
/// <param name="Visibility">Public or Internal.</param>
/// <param name="Slug">Optional; derived from the title when empty.</param>
public sealed record KnowledgeArticleRequest(
    string Title,
    string? Slug,
    string? Summary,
    string Body,
    string Type,
    string Language,
    Guid? CategoryId,
    IReadOnlyList<string>? Tags,
    string Visibility,
    Guid? TranslationOfId);

public sealed record KnowledgeArticleListItemResponse(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string Type,
    string Language,
    Guid? CategoryId,
    string? CategoryName,
    string Status,
    string Visibility,
    int ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt);

public sealed record KnowledgeArticleResponse(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string Body,
    string Type,
    string Language,
    Guid? CategoryId,
    string? CategoryName,
    IReadOnlyList<string> Tags,
    string Status,
    string Visibility,
    Guid? TranslationOfId,
    IReadOnlyList<ArticleTranslationResponse> Translations,
    int ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ArticleTranslationResponse(Guid Id, string Language, string Title, string Slug);

public sealed record ArticleFeedbackRequest(bool Helpful);
