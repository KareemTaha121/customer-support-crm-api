using CustomerSupportCrm.Application.Abstractions.Persistence;
using Npgsql;

namespace CustomerSupportCrm.Infrastructure.Persistence;

internal sealed class PostgresSequenceGenerator(SqlRunner sql) : ISequenceGenerator
{
    public async Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken) =>
        await sql.ScalarAsync<long>("SELECT nextval(@name::regclass)", [new NpgsqlParameter("name", sequenceName)], cancellationToken);
}
