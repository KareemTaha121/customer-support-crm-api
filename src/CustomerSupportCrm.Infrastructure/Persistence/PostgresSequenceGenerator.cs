using CustomerSupportCrm.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

internal sealed class PostgresSequenceGenerator(ApplicationDbContext db) : ISequenceGenerator
{
    public async Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<long>($"SELECT nextval({sequenceName}::regclass) AS \"Value\"")
            .SingleAsync(cancellationToken);
}
