namespace CustomerSupportCrm.Contracts.Common;

public sealed record PaginationMeta(int Page, int PageSize, long TotalCount, int TotalPages)
{
    public static PaginationMeta Create(int page, int pageSize, long totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

        var totalPages = (int)((totalCount + pageSize - 1) / pageSize);
        return new PaginationMeta(page, pageSize, totalCount, totalPages);
    }
}
