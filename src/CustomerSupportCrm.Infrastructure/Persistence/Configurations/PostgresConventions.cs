using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal static class PostgresConventions
{
    /// <summary>
    /// Uses PostgreSQL's system column xmin as an optimistic concurrency token
    /// (no extra column; changes on every row update).
    /// </summary>
    public static void HasXminConcurrencyToken<T>(this EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
}
