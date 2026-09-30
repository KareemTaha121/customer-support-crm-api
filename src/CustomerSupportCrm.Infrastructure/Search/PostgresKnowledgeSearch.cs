using CustomerSupportCrm.Application.Abstractions.Search;
using CustomerSupportCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Search;

/// <summary>
/// PostgreSQL full-text search with the 'simple' configuration (works for Arabic and English,
/// no stemming) and prefix matching on every term. Backed by a GIN expression index created in
/// the migration (<see cref="SearchExpression"/> must match it exactly).
/// </summary>
internal sealed class PostgresKnowledgeSearch(ApplicationDbContext db) : IKnowledgeSearch
{
    public const string SearchExpression = "to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(body, ''))";

    public async Task<IReadOnlyList<Guid>> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var terms = request.Text
            .Split((char[])[' ', '\t', '\n', '\r', ',', '.', ';', ':', '?', '!', '-', '/', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string([.. t.Where(char.IsLetterOrDigit)]))
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(10)
            .ToList();

        if (terms.Count == 0)
        {
            return [];
        }

        // Terms are letters/digits only, so the query string cannot inject tsquery operators.
        var tsQuery = string.Join(" & ", terms.Select(t => t + ":*"));
        var language = request.Language;
        var publicOnly = request.PublicOnly;
        var publishedOnly = request.PublishedOnly || request.PublicOnly;
        var limit = Math.Clamp(request.Limit, 1, 100);

        return await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value"
            FROM knowledge_articles
            WHERE NOT is_deleted
              AND status <> 'Archived'
              AND (NOT {publishedOnly} OR status = 'Published')
              AND (NOT {publicOnly} OR visibility = 'Public')
              AND ({language}::text IS NULL OR language = {language})
              AND to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(body, '')) @@ to_tsquery('simple', {tsQuery})
            ORDER BY ts_rank(to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(body, '')), to_tsquery('simple', {tsQuery})) DESC,
                     view_count DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);
    }
}
