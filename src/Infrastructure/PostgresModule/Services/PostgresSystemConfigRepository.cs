using Microsoft.Extensions.Logging;
using System.Text.Json;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Npgsql;

namespace MattGPT.PostgresModule.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="ISystemConfigRepository"/>.
/// Maintains a single row keyed by the fixed ID "system-config" in a <c>key_value_docs</c> table.
/// </summary>
public class PostgresSystemConfigRepository(NpgsqlDataSource dataSource, ILogger<PostgresSystemConfigRepository> logger)
    : ISystemConfigRepository
{
    private const string TableName = "key_value_docs";
    private const string DocId = "system-config";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<SystemConfig?> GetAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT data FROM {TableName} WHERE id = $1");
        cmd.Parameters.AddWithValue(DocId);

        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<SystemConfig>(json, SerializerOptions);
    }

    public async Task UpsertAsync(SystemConfig config, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var data = JsonSerializer.Serialize(config, SerializerOptions);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName} (id, data) VALUES ($1, $2::jsonb)
            ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data
            """);
        cmd.Parameters.AddWithValue(DocId);
        cmd.Parameters.AddWithValue(data);

        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogDebug("Upserted system config.");
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
                    id   TEXT PRIMARY KEY,
                    data JSONB NOT NULL
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
