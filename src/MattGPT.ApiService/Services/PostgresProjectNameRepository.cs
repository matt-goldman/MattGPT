using Npgsql;

namespace MattGPT.ApiService.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IProjectNameRepository"/>.
/// Stores user-assigned project names in a <c>project_names</c> table.
/// </summary>
public class PostgresProjectNameRepository(NpgsqlDataSource dataSource, ILogger<PostgresProjectNameRepository> logger)
    : IProjectNameRepository
{
    private const string TableName = "project_names";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc/>
    public async Task SetNameAsync(string templateId, string name, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName} (template_id, name, updated_at)
            VALUES ($1, $2, $3)
            ON CONFLICT (template_id) DO UPDATE SET
                name       = EXCLUDED.name,
                updated_at = EXCLUDED.updated_at
            """);
        cmd.Parameters.AddWithValue(templateId);
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.UtcDateTime);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> GetAllNamesAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT template_id, name FROM {TableName}");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new Dictionary<string, string>();

        while (await reader.ReadAsync(ct))
            results[reader.GetString(0)] = reader.GetString(1);

        return results;
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaEnsured) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;

            await using var cmd = dataSource.CreateCommand(
                $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    template_id TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )
                """);

            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Ensured Postgres {Table} table schema.", TableName);
            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
