using CustomerSupportCrm.Application.Abstractions.Search;
using CustomerSupportCrm.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace CustomerSupportCrm.Infrastructure.Search;

/// <summary>
/// PostgreSQL full-text search with the 'simple' configuration (works for Arabic and English,
/// no stemming) and prefix matching on every term. Backed by a GIN expression index created in
/// the migration; <see cref="SearchVector"/> must match that index expression exactly.
/// </summary>
internal sealed class PostgresKnowledgeSearch(SqlRunner sql) : IKnowledgeSearch
{
    public const string SearchVector = "to_tsvector('simple'::regconfig, coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(body, ''))";

    private const string Query = $"""
        SELECT id
        FROM knowledge_articles
        WHERE NOT is_deleted
          AND status <> 'Archived'
          AND (NOT @publishedOnly OR status = 'Published')
          AND (NOT @publicOnly OR visibility = 'Public')
          AND (@language::text IS NULL OR language = @language)
          AND {SearchVector} @@ to_tsquery('simple'::regconfig, @query)
        ORDER BY ts_rank({SearchVector}, to_tsquery('simple'::regconfig, @query)) DESC, view_count DESC
        LIMIT @limit
        """;

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

        // Terms are letters/digits only, so they cannot inject tsquery operators.
        var tsQuery = string.Join(request.MatchAny ? " | " : " & ", terms.Select(t => t + ":*"));

        return await sql.QueryAsync(
            Query,
            [
                new NpgsqlParameter("publishedOnly", request.PublishedOnly || request.PublicOnly),
                new NpgsqlParameter("publicOnly", request.PublicOnly),
                new NpgsqlParameter("language", NpgsqlDbType.Text) { Value = (object?)request.Language ?? DBNull.Value },
                new NpgsqlParameter("query", tsQuery),
                new NpgsqlParameter("limit", Math.Clamp(request.Limit, 1, 100)),
            ],
            reader => reader.GetGuid(0),
            cancellationToken);
    }
}
