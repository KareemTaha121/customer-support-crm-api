using CustomerSupportCrm.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Common.Pagination;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, PaginationMeta Meta)
{
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new([.. Items.Select(map)], Meta);
}

public static class PaginationExtensions
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Counts and pages in the database. The query must already be ordered.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, PaginationMeta.Create(page, pageSize, totalCount));
    }
}
