using System.Text.Json;
using MattGPT.ApiService.Models;
using Npgsql;

namespace MattGPT.ApiService.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IUserProfileRepository"/>.
/// Maintains a single row keyed by the fixed ID "user-profile" in a <c>key_value_docs</c> table.
/// </summary>
public class PostgresUserProfileRepository(NpgsqlDataSource dataSource, ILogger<PostgresUserProfileRepository> logger)
    : IUserProfileRepository
{
    private const string TableName = "key_value_docs";
    private const string DocId = "user-profile";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<UserProfile?> GetAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT data FROM {TableName} WHERE id = $1");
        cmd.Parameters.AddWithValue(DocId);

        var json = (string?)await cmd.ExecuteScalarAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<UserProfile>(json, SerializerOptions);
    }

    public async Task UpsertAsync(UserProfile profile, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var data = JsonSerializer.Serialize(profile, SerializerOptions);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName} (id, data) VALUES ($1, $2::jsonb)
            ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data
            """);
        cmd.Parameters.AddWithValue(DocId);
        cmd.Parameters.AddWithValue(data);

        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogDebug("Upserted user profile (source create_time: {CreateTime}).", profile.SourceCreateTime);
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
