using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Application.Tests.Contracts;

public sealed class PaginationMetaTests
{
    [Theory]
    [InlineData(1, 25, 0, 0)]
    [InlineData(1, 25, 1, 1)]
    [InlineData(1, 25, 250, 10)]
    [InlineData(1, 25, 251, 11)]
    public void CalculatesTotalPages(int page, int pageSize, long totalCount, int expectedTotalPages)
    {
        var meta = PaginationMeta.Create(page, pageSize, totalCount);

        Assert.Equal(expectedTotalPages, meta.TotalPages);
    }

    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 25, -1)]
    public void RejectsInvalidArguments(int page, int pageSize, long totalCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaginationMeta.Create(page, pageSize, totalCount));
    }
}
