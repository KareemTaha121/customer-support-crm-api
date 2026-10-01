using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Abstractions.Search;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.KnowledgeBase;
using CustomerSupportCrm.Domain.KnowledgeBase;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.KnowledgeBase;

public static class KnowledgeErrors
{
    public const string ArticleNotFound = "ARTICLE_NOT_FOUND";
    public const string CategoryNotFound = "KB_CATEGORY_NOT_FOUND";
    public const string SlugTaken = "ARTICLE_SLUG_TAKEN";
    public const string CategoryInUse = "KB_CATEGORY_IN_USE";
}

internal static class KnowledgeQueries
{
    public static NotFoundException NotFound() => new(KnowledgeErrors.ArticleNotFound, "The article was not found.");

    public static IQueryable<KnowledgeArticleListItemResponse> ProjectList(IQueryable<KnowledgeArticle> articles, IApplicationDbContext db) =>
        articles.Select(a => new KnowledgeArticleListItemResponse(
            a.Id,
            a.Title,
            a.Slug,
            a.Summary,
            a.Type.ToString(),
            a.Language,
            a.CategoryId,
            db.KnowledgeCategories.Where(c => c.Id == a.CategoryId).Select(c => c.Name).FirstOrDefault(),
            a.Status.ToString(),
            a.Visibility.ToString(),
            a.ViewCount,
            a.HelpfulCount,
            a.NotHelpfulCount,
            a.PublishedAt,
            a.UpdatedAt));

    /// <param name="publicOnly">Customer view: only published, public translations are listed.</param>
    public static async Task<KnowledgeArticleResponse> GetResponseAsync(IApplicationDbContext db, IQueryable<KnowledgeArticle> source, CancellationToken cancellationToken, bool publicOnly = false)
    {
        var a = await source.AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        var groupId = a.TranslationOfId ?? a.Id;
        var translations = await db.KnowledgeArticles.AsNoTracking()
            .Where(x => x.Id != a.Id && (x.Id == groupId || x.TranslationOfId == groupId))
            .Where(x => !publicOnly || (x.Status == ArticleStatus.Published && x.Visibility == ArticleVisibility.Public))
            .Select(x => new ArticleTranslationResponse(x.Id, x.Language, x.Title, x.Slug))
            .ToListAsync(cancellationToken);
        var categoryName = await db.KnowledgeCategories.Where(c => c.Id == a.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

        return new KnowledgeArticleResponse(
            a.Id, a.Title, a.Slug, a.Summary, a.Body, a.Type.ToString(), a.Language, a.CategoryId, categoryName, a.Tags,
            a.Status.ToString(), a.Visibility.ToString(), a.TranslationOfId, translations, a.ViewCount, a.HelpfulCount, a.NotHelpfulCount,
            a.PublishedAt, a.CreatedAt, a.UpdatedAt);
    }

    /// <summary>Search-ranked page when text is given; newest first otherwise.</summary>
    public static async Task<PagedResult<KnowledgeArticleListItemResponse>> SearchAsync(
        IApplicationDbContext db,
        IKnowledgeSearch search,
        IQueryable<KnowledgeArticle> filtered,
        string? text,
        string? language,
        bool publicOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return await ProjectList(filtered.OrderByDescending(a => a.PublishedAt ?? a.CreatedAt).ThenBy(a => a.Id), db)
                .ToPagedResultAsync(page, pageSize, cancellationToken);
        }

        var ranked = await search.SearchAsync(new KnowledgeSearchRequest(text, language, publicOnly, publicOnly, 100), cancellationToken);
        var matching = await ProjectList(filtered.Where(a => ranked.Contains(a.Id)), db).ToListAsync(cancellationToken);
        var ordered = matching.OrderBy(a => ranked.ToList().IndexOf(a.Id)).ToList();

        return new PagedResult<KnowledgeArticleListItemResponse>(
            [.. ordered.Skip((page - 1) * pageSize).Take(pageSize)],
            PaginationMeta.Create(page, pageSize, ordered.Count));
    }
}

// ---------- Categories ----------

public sealed record ListKnowledgeCategoriesQuery(bool PublicOnly) : IRequest<IReadOnlyList<KnowledgeCategoryResponse>>;

internal sealed class ListKnowledgeCategoriesHandler(IApplicationDbContext db) : IRequestHandler<ListKnowledgeCategoriesQuery, IReadOnlyList<KnowledgeCategoryResponse>>
{
    public async Task<IReadOnlyList<KnowledgeCategoryResponse>> Handle(ListKnowledgeCategoriesQuery request, CancellationToken cancellationToken) =>
        await db.KnowledgeCategories.AsNoTracking()
            .Where(c => !request.PublicOnly || c.IsPublic)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new KnowledgeCategoryResponse(
                c.Id, c.Name, c.NameAr, c.Description, c.ParentId, c.SortOrder, c.IsPublic,
                db.KnowledgeArticles.Count(a => a.CategoryId == c.Id && (!request.PublicOnly || (a.Status == ArticleStatus.Published && a.Visibility == ArticleVisibility.Public)))))
            .ToListAsync(cancellationToken);
}

public sealed record SaveKnowledgeCategoryCommand(Guid? CategoryId, KnowledgeCategoryRequest Category) : IRequest<KnowledgeCategoryResponse>;

internal sealed class SaveKnowledgeCategoryValidator : AbstractValidator<SaveKnowledgeCategoryCommand>
{
    public SaveKnowledgeCategoryValidator()
    {
        RuleFor(c => c.Category.Name).NotEmpty().MaximumLength(KnowledgeCategory.NameMaxLength);
        RuleFor(c => c.Category.NameAr).MaximumLength(KnowledgeCategory.NameMaxLength);
        RuleFor(c => c.Category.Description).MaximumLength(1000);
    }
}

internal sealed class SaveKnowledgeCategoryHandler(IApplicationDbContext db) : IRequestHandler<SaveKnowledgeCategoryCommand, KnowledgeCategoryResponse>
{
    public async Task<KnowledgeCategoryResponse> Handle(SaveKnowledgeCategoryCommand request, CancellationToken cancellationToken)
    {
        var input = request.Category;
        KnowledgeCategory category;
        if (request.CategoryId is { } id)
        {
            category = await db.KnowledgeCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new NotFoundException(KnowledgeErrors.CategoryNotFound, "The category was not found.");
        }
        else
        {
            category = KnowledgeCategory.Create();
            db.KnowledgeCategories.Add(category);
        }

        // Same rules as ticket categories: the parent must exist (a missing one used to fail at the
        // foreign key) and must not be the category or one of its subcategories.
        if (input.ParentId is { } parentId && parentId != category.ParentId)
        {
            var parents = await db.KnowledgeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);
            if (!parents.ContainsKey(parentId))
            {
                throw new NotFoundException(KnowledgeErrors.CategoryNotFound, "The parent category was not found.");
            }

            if (CategoryHierarchy.CreatesCycle(category.Id, parentId, parents))
            {
                throw CategoryHierarchy.CycleError();
            }
        }

        category.Update(input.Name, input.NameAr, input.Description, input.ParentId, input.SortOrder, input.IsPublic);
        await db.SaveChangesAsync(cancellationToken);
        return new KnowledgeCategoryResponse(category.Id, category.Name, category.NameAr, category.Description, category.ParentId, category.SortOrder, category.IsPublic, 0);
    }
}

public sealed record DeleteKnowledgeCategoryCommand(Guid CategoryId) : IRequest;

internal sealed class DeleteKnowledgeCategoryHandler(IApplicationDbContext db) : IRequestHandler<DeleteKnowledgeCategoryCommand>
{
    public async Task Handle(DeleteKnowledgeCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.KnowledgeCategories.SingleOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken)
            ?? throw new NotFoundException(KnowledgeErrors.CategoryNotFound, "The category was not found.");
        if (await db.KnowledgeCategories.AnyAsync(c => c.ParentId == category.Id, cancellationToken))
        {
            throw new ConflictException(KnowledgeErrors.CategoryInUse, "Move or delete the sub-categories first.");
        }

        db.KnowledgeCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Articles (staff) ----------

public sealed record ListKnowledgeArticlesQuery(
    int Page = 1,
    int PageSize = PaginationExtensions.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Type = null,
    string? Language = null,
    string? Visibility = null,
    Guid? CategoryId = null) : IRequest<PagedResult<KnowledgeArticleListItemResponse>>;

internal sealed class ListKnowledgeArticlesValidator : AbstractValidator<ListKnowledgeArticlesQuery>
{
    public ListKnowledgeArticlesValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).Must(v => v is null || Enum.TryParse<ArticleStatus>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Type).Must(v => v is null || Enum.TryParse<ArticleType>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Visibility).Must(v => v is null || Enum.TryParse<ArticleVisibility>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Language).OneOf("en", "ar");
    }
}

internal sealed class ListKnowledgeArticlesHandler(IApplicationDbContext db, IKnowledgeSearch search)
    : IRequestHandler<ListKnowledgeArticlesQuery, PagedResult<KnowledgeArticleListItemResponse>>
{
    public Task<PagedResult<KnowledgeArticleListItemResponse>> Handle(ListKnowledgeArticlesQuery request, CancellationToken cancellationToken)
    {
        var query = db.KnowledgeArticles.AsNoTracking();
        if (request.Status is { } status)
        {
            var value = Enum.Parse<ArticleStatus>(status);
            query = query.Where(a => a.Status == value);
        }

        if (request.Type is { } type)
        {
            var value = Enum.Parse<ArticleType>(type);
            query = query.Where(a => a.Type == value);
        }

        if (request.Visibility is { } visibility)
        {
            var value = Enum.Parse<ArticleVisibility>(visibility);
            query = query.Where(a => a.Visibility == value);
        }

        if (request.Language is { } language)
        {
            query = query.Where(a => a.Language == language);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(a => a.CategoryId == categoryId);
        }

        return KnowledgeQueries.SearchAsync(db, search, query, request.Search, request.Language, publicOnly: false, request.Page, request.PageSize, cancellationToken);
    }
}

public sealed record GetKnowledgeArticleQuery(Guid ArticleId) : IRequest<KnowledgeArticleResponse>;

internal sealed class GetKnowledgeArticleHandler(IApplicationDbContext db) : IRequestHandler<GetKnowledgeArticleQuery, KnowledgeArticleResponse>
{
    public Task<KnowledgeArticleResponse> Handle(GetKnowledgeArticleQuery request, CancellationToken cancellationToken) =>
        KnowledgeQueries.GetResponseAsync(db, db.KnowledgeArticles.Where(a => a.Id == request.ArticleId), cancellationToken);
}

public sealed record SaveKnowledgeArticleCommand(Guid? ArticleId, KnowledgeArticleRequest Article) : IRequest<KnowledgeArticleResponse>;

internal sealed class SaveKnowledgeArticleValidator : AbstractValidator<SaveKnowledgeArticleCommand>
{
    public SaveKnowledgeArticleValidator()
    {
        RuleFor(c => c.Article.Title).NotEmpty().MaximumLength(KnowledgeArticle.TitleMaxLength);
        RuleFor(c => c.Article.Slug).MaximumLength(KnowledgeArticle.SlugMaxLength);
        RuleFor(c => c.Article.Summary).MaximumLength(KnowledgeArticle.SummaryMaxLength);
        RuleFor(c => c.Article.Body).NotNull().MaximumLength(KnowledgeArticle.BodyMaxLength);
        RuleFor(c => c.Article.Type).IsEnumName(typeof(ArticleType));
        RuleFor(c => c.Article.Visibility).IsEnumName(typeof(ArticleVisibility));
        RuleFor(c => c.Article.Language).Must(l => l is "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
    }
}

internal sealed class SaveKnowledgeArticleHandler(IApplicationDbContext db, IAuditTrail audit) : IRequestHandler<SaveKnowledgeArticleCommand, KnowledgeArticleResponse>
{
    public async Task<KnowledgeArticleResponse> Handle(SaveKnowledgeArticleCommand request, CancellationToken cancellationToken)
    {
        var input = request.Article;
        var slug = KnowledgeArticle.Slugify(string.IsNullOrWhiteSpace(input.Slug) ? input.Title : input.Slug);
        if (await db.KnowledgeArticles.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug && a.Id != request.ArticleId, cancellationToken))
        {
            throw new ConflictException(KnowledgeErrors.SlugTaken, "Another article already uses this URL slug.");
        }

        if (input.CategoryId is { } categoryId && !await db.KnowledgeCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new NotFoundException(KnowledgeErrors.CategoryNotFound, "The category was not found.");
        }

        KnowledgeArticle article;
        if (request.ArticleId is { } id)
        {
            article = await db.KnowledgeArticles.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw KnowledgeQueries.NotFound();
            article.Edit(input.Title, slug, input.Summary, input.Body, Enum.Parse<ArticleType>(input.Type), input.Language, input.CategoryId, input.Tags ?? [], Enum.Parse<ArticleVisibility>(input.Visibility));
        }
        else
        {
            article = KnowledgeArticle.CreateDraft(input.Title, slug, input.Summary, input.Body, Enum.Parse<ArticleType>(input.Type), input.Language, input.CategoryId, input.Tags ?? [], Enum.Parse<ArticleVisibility>(input.Visibility), input.TranslationOfId);
            db.KnowledgeArticles.Add(article);
        }

        audit.Record(request.ArticleId is null ? "kb.article_created" : "kb.article_updated", "KnowledgeArticle", article.Id.ToString(), newValues: new { article.Title, article.Slug, article.Language });
        await db.SaveChangesAsync(cancellationToken);
        return await KnowledgeQueries.GetResponseAsync(db, db.KnowledgeArticles.Where(a => a.Id == article.Id), cancellationToken);
    }
}

/// <param name="Action">publish, unpublish, archive, restore or delete.</param>
public sealed record ChangeKnowledgeArticleCommand(Guid ArticleId, string Action) : IRequest<KnowledgeArticleResponse?>;

internal sealed class ChangeKnowledgeArticleHandler(IApplicationDbContext db, ICurrentUser currentUser, IAuditTrail audit, TimeProvider time)
    : IRequestHandler<ChangeKnowledgeArticleCommand, KnowledgeArticleResponse?>
{
    public async Task<KnowledgeArticleResponse?> Handle(ChangeKnowledgeArticleCommand request, CancellationToken cancellationToken)
    {
        var article = await db.KnowledgeArticles.SingleOrDefaultAsync(a => a.Id == request.ArticleId, cancellationToken) ?? throw KnowledgeQueries.NotFound();
        switch (request.Action)
        {
            case "publish":
                article.Publish(time.GetUtcNow(), currentUser.UserId.Value);
                break;
            case "unpublish":
                article.Unpublish();
                break;
            case "archive":
                article.Archive();
                break;
            case "restore":
                article.Restore();
                break;
            case "delete":
                db.KnowledgeArticles.Remove(article);
                break;
        }

        audit.Record($"kb.article_{request.Action}", "KnowledgeArticle", article.Id.ToString(), newValues: new { article.Title });
        await db.SaveChangesAsync(cancellationToken);
        return request.Action == "delete" ? null : await KnowledgeQueries.GetResponseAsync(db, db.KnowledgeArticles.Where(a => a.Id == article.Id), cancellationToken);
    }
}

/// <summary>Published articles relevant to a ticket's subject/description (agent answer suggestions).</summary>
public sealed record SuggestArticlesQuery(Guid TicketId, int Limit = 5) : IRequest<IReadOnlyList<KnowledgeArticleListItemResponse>>;

internal sealed class SuggestArticlesHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IKnowledgeSearch search)
    : IRequestHandler<SuggestArticlesQuery, IReadOnlyList<KnowledgeArticleListItemResponse>>
{
    public async Task<IReadOnlyList<KnowledgeArticleListItemResponse>> Handle(SuggestArticlesQuery request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await db.Tickets.AsNoTracking().Where(t => t.Id == request.TicketId).WhereInScope(scope).Select(t => new { t.Subject })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(Tickets.Common.TicketErrors.TicketNotFound, "The ticket was not found.");

        var ids = await search.SearchAsync(new KnowledgeSearchRequest(ticket.Subject, null, false, true, Math.Clamp(request.Limit, 1, 20), MatchAny: true), cancellationToken);
        var items = await KnowledgeQueries.ProjectList(db.KnowledgeArticles.AsNoTracking().Where(a => ids.Contains(a.Id)), db).ToListAsync(cancellationToken);
        return [.. items.OrderBy(a => ids.ToList().IndexOf(a.Id))];
    }
}

internal sealed class KnowledgeBaseEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/kb").WithTags("Knowledge base");

        group.MapGet("/categories", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListKnowledgeCategoriesQuery(false), ct)))
            .RequireAuthorization(Permissions.KnowledgeView)
            .WithName("ListKnowledgeCategories")
            .Produces<ApiResponse<IReadOnlyList<KnowledgeCategoryResponse>>>();
        group.MapPost("/categories", async (KnowledgeCategoryRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/kb/categories", await sender.Send(new SaveKnowledgeCategoryCommand(null, request), ct)))
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("CreateKnowledgeCategory")
            .Produces<ApiResponse<KnowledgeCategoryResponse>>(StatusCodes.Status201Created);
        group.MapPut("/categories/{id:guid}", async (Guid id, KnowledgeCategoryRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveKnowledgeCategoryCommand(id, request), ct)))
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("UpdateKnowledgeCategory")
            .Produces<ApiResponse<KnowledgeCategoryResponse>>();
        group.MapDelete("/categories/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteKnowledgeCategoryCommand(id), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("DeleteKnowledgeCategory")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/articles", async ([AsParameters] ListKnowledgeArticlesQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.KnowledgeView)
            .WithName("ListKnowledgeArticles")
            .Produces<ApiResponse<IReadOnlyList<KnowledgeArticleListItemResponse>>>();
        group.MapGet("/articles/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetKnowledgeArticleQuery(id), ct)))
            .RequireAuthorization(Permissions.KnowledgeView)
            .WithName("GetKnowledgeArticle")
            .Produces<ApiResponse<KnowledgeArticleResponse>>();
        group.MapPost("/articles", async (KnowledgeArticleRequest request, ISender sender, CancellationToken ct) =>
            {
                var article = await sender.Send(new SaveKnowledgeArticleCommand(null, request), ct);
                return ApiResults.Created($"/api/v1/kb/articles/{article.Id}", article);
            })
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("CreateKnowledgeArticle")
            .Produces<ApiResponse<KnowledgeArticleResponse>>(StatusCodes.Status201Created);
        group.MapPut("/articles/{id:guid}", async (Guid id, KnowledgeArticleRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveKnowledgeArticleCommand(id, request), ct)))
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("UpdateKnowledgeArticle")
            .Produces<ApiResponse<KnowledgeArticleResponse>>();

        foreach (var (action, permission) in new[]
                 {
                     ("publish", Permissions.KnowledgePublish),
                     ("unpublish", Permissions.KnowledgePublish),
                     ("archive", Permissions.KnowledgeManage),
                     ("restore", Permissions.KnowledgeManage),
                 })
        {
            group.MapPost($"/articles/{{id:guid}}/{action}", async (Guid id, ISender sender, CancellationToken ct) =>
                    ApiResults.Ok(await sender.Send(new ChangeKnowledgeArticleCommand(id, action), ct)))
                .RequireAuthorization(permission)
                .WithName($"{char.ToUpperInvariant(action[0])}{action[1..]}KnowledgeArticle")
                .Produces<ApiResponse<KnowledgeArticleResponse>>();
        }

        group.MapDelete("/articles/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ChangeKnowledgeArticleCommand(id, "delete"), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.KnowledgeManage)
            .WithName("DeleteKnowledgeArticle")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/suggestions", async (Guid ticketId, int? limit, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SuggestArticlesQuery(ticketId, limit ?? 5), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("SuggestKnowledgeArticles")
            .Produces<ApiResponse<IReadOnlyList<KnowledgeArticleListItemResponse>>>();
    }
}

// ---------- Public (customer portal FAQ) ----------

public sealed record ListPublicArticlesQuery(int Page = 1, int PageSize = 20, string? Search = null, string? Type = null, string? Language = null, Guid? CategoryId = null)
    : IRequest<PagedResult<KnowledgeArticleListItemResponse>>;

internal sealed class ListPublicArticlesValidator : AbstractValidator<ListPublicArticlesQuery>
{
    public ListPublicArticlesValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Type).Must(v => v is null || Enum.TryParse<ArticleType>(v, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Language).OneOf("en", "ar");
    }
}

internal sealed class ListPublicArticlesHandler(IApplicationDbContext db, IKnowledgeSearch search)
    : IRequestHandler<ListPublicArticlesQuery, PagedResult<KnowledgeArticleListItemResponse>>
{
    public Task<PagedResult<KnowledgeArticleListItemResponse>> Handle(ListPublicArticlesQuery request, CancellationToken cancellationToken)
    {
        var query = db.KnowledgeArticles.AsNoTracking().Where(a => a.Status == ArticleStatus.Published && a.Visibility == ArticleVisibility.Public);
        if (request.Type is { } type)
        {
            var value = Enum.Parse<ArticleType>(type);
            query = query.Where(a => a.Type == value);
        }

        if (request.Language is { } language)
        {
            query = query.Where(a => a.Language == language);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(a => a.CategoryId == categoryId);
        }

        return KnowledgeQueries.SearchAsync(db, search, query, request.Search, request.Language, publicOnly: true, request.Page, request.PageSize, cancellationToken);
    }
}

public sealed record GetPublicArticleQuery(string Slug) : IRequest<KnowledgeArticleResponse>;

internal sealed class GetPublicArticleHandler(IApplicationDbContext db) : IRequestHandler<GetPublicArticleQuery, KnowledgeArticleResponse>
{
    public async Task<KnowledgeArticleResponse> Handle(GetPublicArticleQuery request, CancellationToken cancellationToken)
    {
        var article = await db.KnowledgeArticles.SingleOrDefaultAsync(a => a.Slug == request.Slug && a.Status == ArticleStatus.Published && a.Visibility == ArticleVisibility.Public, cancellationToken)
            ?? throw KnowledgeQueries.NotFound();
        article.RecordView();
        await db.SaveChangesAsync(cancellationToken);

        return await KnowledgeQueries.GetResponseAsync(db, db.KnowledgeArticles.Where(a => a.Id == article.Id), cancellationToken, publicOnly: true);
    }
}

public sealed record SubmitArticleFeedbackCommand(Guid ArticleId, bool Helpful) : IRequest;

internal sealed class SubmitArticleFeedbackHandler(IApplicationDbContext db) : IRequestHandler<SubmitArticleFeedbackCommand>
{
    public async Task Handle(SubmitArticleFeedbackCommand request, CancellationToken cancellationToken)
    {
        // Anonymous endpoint: only articles the help center shows can be rated (internal ones are 404, as on GET).
        var article = await db.KnowledgeArticles.SingleOrDefaultAsync(
                a => a.Id == request.ArticleId && a.Status == ArticleStatus.Published && a.Visibility == ArticleVisibility.Public,
                cancellationToken)
            ?? throw KnowledgeQueries.NotFound();
        article.RecordFeedback(request.Helpful);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class PublicKnowledgeBaseEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/kb");
        group.MapGet("/categories", async (ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListKnowledgeCategoriesQuery(true), ct)))
            .WithName("ListPublicKnowledgeCategories")
            .Produces<ApiResponse<IReadOnlyList<KnowledgeCategoryResponse>>>();
        group.MapGet("/articles", async ([AsParameters] ListPublicArticlesQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .WithName("ListPublicKnowledgeArticles")
            .Produces<ApiResponse<IReadOnlyList<KnowledgeArticleListItemResponse>>>();
        group.MapGet("/articles/{slug}", async (string slug, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetPublicArticleQuery(slug), ct)))
            .WithName("GetPublicKnowledgeArticle")
            .Produces<ApiResponse<KnowledgeArticleResponse>>();
        group.MapPost("/articles/{id:guid}/feedback", async (Guid id, ArticleFeedbackRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SubmitArticleFeedbackCommand(id, request.Helpful), ct);
                return ApiResults.Success();
            })
            .RequireRateLimiting(RateLimitPolicies.ArticleFeedback)
            .WithName("SubmitKnowledgeArticleFeedback")
            .Produces<ApiResponse<object?>>();
    }
}
