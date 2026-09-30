using System.Text.RegularExpressions;
using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.KnowledgeBase;

public enum ArticleType
{
    Faq,
    Article,
    Guide,
    Solution,
}

public enum ArticleStatus
{
    Draft,
    Published,
    Archived,
}

public enum ArticleVisibility
{
    /// <summary>Customer portal, chatbot and staff.</summary>
    Public,

    /// <summary>Staff only.</summary>
    Internal,
}

/// <summary>
/// A knowledge base entry (FAQ, article, guide or solution) in one language. Publishing is a
/// domain action; only published public articles reach customers.
/// </summary>
public sealed partial class KnowledgeArticle : AggregateRoot<Guid>, IAuditableEntity, ISoftDeletable
{
    public const int TitleMaxLength = 300;
    public const int SlugMaxLength = 200;
    public const int SummaryMaxLength = 1000;
    public const int BodyMaxLength = 200_000;

    public const string InvalidCode = "INVALID_ARTICLE";
    public const string InvalidStateCode = "INVALID_ARTICLE_STATE";

    private KnowledgeArticle()
    {
        Title = string.Empty;
        Slug = string.Empty;
        Body = string.Empty;
        Language = "en";
        Tags = [];
    }

    private KnowledgeArticle(Guid id)
        : base(id)
    {
        Title = string.Empty;
        Slug = string.Empty;
        Body = string.Empty;
        Language = "en";
        Tags = [];
    }

    public string Title { get; private set; }

    /// <summary>URL identifier, unique across articles.</summary>
    public string Slug { get; private set; }

    public string? Summary { get; private set; }

    /// <summary>Markdown.</summary>
    public string Body { get; private set; }

    public ArticleType Type { get; private set; }

    public string Language { get; private set; }

    public Guid? CategoryId { get; private set; }

    public List<string> Tags { get; private set; }

    public ArticleVisibility Visibility { get; private set; }

    public ArticleStatus Status { get; private set; }

    /// <summary>Links translations of the same content (e.g. the Arabic version of an English article).</summary>
    public Guid? TranslationOfId { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public Guid? PublishedBy { get; private set; }

    public int ViewCount { get; private set; }

    public int HelpfulCount { get; private set; }

    public int NotHelpfulCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public static KnowledgeArticle CreateDraft(
        string title,
        string slug,
        string? summary,
        string body,
        ArticleType type,
        string language,
        Guid? categoryId,
        IEnumerable<string> tags,
        ArticleVisibility visibility,
        Guid? translationOfId)
    {
        var article = new KnowledgeArticle(Guid.CreateVersion7()) { Status = ArticleStatus.Draft, TranslationOfId = translationOfId };
        article.Edit(title, slug, summary, body, type, language, categoryId, tags, visibility);
        return article;
    }

    /// <summary>Lower-case, dash-separated; letters of any script are kept (Arabic slugs are valid).</summary>
    public static string Slugify(string text)
    {
#pragma warning disable CA1308 // Slugs are conventionally lower case.
        var slug = NonSlug().Replace((text ?? string.Empty).Trim().ToLowerInvariant(), "-").Trim('-');
#pragma warning restore CA1308
        return slug.Length > SlugMaxLength ? slug[..SlugMaxLength].Trim('-') : slug;
    }

    public void Edit(string title, string slug, string? summary, string body, ArticleType type, string language, Guid? categoryId, IEnumerable<string> tags, ArticleVisibility visibility)
    {
        if (Status == ArticleStatus.Archived)
        {
            throw new DomainException(InvalidStateCode, "Restore the article before editing it.");
        }

        var trimmedTitle = title?.Trim();
        var normalizedSlug = Slugify(string.IsNullOrWhiteSpace(slug) ? title ?? string.Empty : slug);
        if (string.IsNullOrEmpty(trimmedTitle) || trimmedTitle.Length > TitleMaxLength || normalizedSlug.Length == 0)
        {
            throw new DomainException(InvalidCode, "The title or slug is not valid.");
        }

        if ((body ?? string.Empty).Length > BodyMaxLength || (summary?.Length ?? 0) > SummaryMaxLength || language is not ("en" or "ar"))
        {
            throw new DomainException(InvalidCode, "The article content is not valid.");
        }

        Title = trimmedTitle;
        Slug = normalizedSlug;
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        Body = body?.Trim() ?? string.Empty;
        Type = type;
        Language = language;
        CategoryId = categoryId;
        Tags = [.. tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 50).Distinct(StringComparer.OrdinalIgnoreCase).Take(20)];
        Visibility = visibility;
    }

    public void Publish(DateTimeOffset now, Guid? publishedBy)
    {
        if (Status == ArticleStatus.Archived)
        {
            throw new DomainException(InvalidStateCode, "Archived articles must be restored before publishing.");
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            throw new DomainException(InvalidStateCode, "An article needs content before it can be published.");
        }

        Status = ArticleStatus.Published;
        PublishedAt = now;
        PublishedBy = publishedBy;
    }

    public void Unpublish()
    {
        if (Status != ArticleStatus.Published)
        {
            throw new DomainException(InvalidStateCode, "Only published articles can be unpublished.");
        }

        Status = ArticleStatus.Draft;
    }

    public void Archive() => Status = ArticleStatus.Archived;

    public void Restore()
    {
        if (Status == ArticleStatus.Archived)
        {
            Status = ArticleStatus.Draft;
        }
    }

    public void RecordView() => ViewCount++;

    public void RecordFeedback(bool helpful)
    {
        if (helpful)
        {
            HelpfulCount++;
        }
        else
        {
            NotHelpfulCount++;
        }
    }

    public bool IsVisibleToCustomers => Status == ArticleStatus.Published && Visibility == ArticleVisibility.Public;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonSlug();
}

/// <summary>A knowledge base category (optionally nested) with Arabic and English names.</summary>
public sealed class KnowledgeCategory : Entity<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 150;
    public const string InvalidCode = "INVALID_KB_CATEGORY";

    private KnowledgeCategory()
    {
        Name = string.Empty;
    }

    private KnowledgeCategory(Guid id)
        : base(id)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public string? NameAr { get; private set; }

    public string? Description { get; private set; }

    public Guid? ParentId { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Shown in the customer portal when true.</summary>
    public bool IsPublic { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static KnowledgeCategory Create() => new(Guid.CreateVersion7());

    public void Update(string name, string? nameAr, string? description, Guid? parentId, int sortOrder, bool isPublic)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength || parentId == Id)
        {
            throw new DomainException(InvalidCode, "The category is not valid.");
        }

        Name = trimmed;
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ParentId = parentId;
        SortOrder = sortOrder;
        IsPublic = isPublic;
    }
}
