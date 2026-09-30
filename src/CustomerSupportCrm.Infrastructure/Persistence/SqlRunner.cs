using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CustomerSupportCrm.Infrastructure.Persistence;

/// <summary>
/// Hand-written SQL through the DbContext's connection (and current transaction). Used where
/// PostgreSQL features matter: full-text search, reporting aggregates, sequences.
/// </summary>
internal sealed class SqlRunner(ApplicationDbContext db)
{
    public async Task<List<T>> QueryAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(sql, parameters, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public async Task<T?> ScalarAsync<T>(string sql, IEnumerable<NpgsqlParameter> parameters, CancellationToken cancellationToken)
    {
        await using var command = await CreateCommandAsync(sql, parameters, cancellationToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default : (T)value;
    }

    private async Task<DbCommand> CreateCommandAsync(string sql, IEnumerable<NpgsqlParameter> parameters, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
#pragma warning disable CA2100 // SQL text is constant; all values are bound parameters.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }
}

internal static class ReaderExtensions
{
    public static double? GetNullableDouble(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    public static string? GetNullableString(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static DateTimeOffset GetUtc(this DbDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc), TimeSpan.Zero);
}
