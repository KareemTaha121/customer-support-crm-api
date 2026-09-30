namespace CustomerSupportCrm.Application.Abstractions.Search;

/// <param name="PublicOnly">Only published, public articles (customers, chatbot).</param>
/// <param name="PublishedOnly">Only published articles (staff answer suggestions).</param>
/// <param name="MatchAny">Match articles containing any term, ranked by relevance (free text such as a
/// ticket subject). Default: every term must match (search boxes).</param>
public sealed record KnowledgeSearchRequest(
    string Text,
    string? Language,
    bool PublicOnly,
    bool PublishedOnly,
    int Limit,
    bool MatchAny = false);

/// <summary>
/// Full-text article search returning ids by relevance. Implemented with PostgreSQL today;
/// replaceable by a search engine without changing feature contracts.
/// </summary>
public interface IKnowledgeSearch
{
    Task<IReadOnlyList<Guid>> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken);
}
