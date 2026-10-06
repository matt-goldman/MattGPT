using Microsoft.Extensions.Logging;
using System.Text.Json;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Npgsql;

namespace MattGPT.PostgresModule.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IChatSessionRepository"/>.
/// Stores chat sessions as JSONB documents in a <c>chat_sessions</c> table.
/// </summary>
public class PostgresChatSessionRepository(NpgsqlDataSource dataSource, ILogger<PostgresChatSessionRepository> logger)
    : IChatSessionRepository
{
    private const string TableName = "chat_sessions";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc/>
    public async Task CreateAsync(ChatSession session, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var data = JsonSerializer.Serialize(session, SerializerOptions);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName}
                (session_id, status, user_id, created_at, updated_at, data)
            VALUES ($1, $2, $3, $4, $5, $6::jsonb)
            """);

        cmd.Parameters.AddWithValue(session.SessionId.ToString());
        cmd.Parameters.AddWithValue(session.Status.ToString());
        cmd.Parameters.AddWithValue((object?)session.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(session.CreatedAt.UtcDateTime);
        cmd.Parameters.AddWithValue(session.UpdatedAt.UtcDateTime);
        cmd.Parameters.AddWithValue(data);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<ChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT data FROM {TableName} WHERE session_id = $1");
        cmd.Parameters.AddWithValue(sessionId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return JsonSerializer.Deserialize<ChatSession>(reader.GetString(0), SerializerOptions);
    }

    /// <inheritdoc/>
    public async Task AddMessageAsync(Guid sessionId, ChatSessionMessage message, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var msgJson = JsonSerializer.Serialize(message, SerializerOptions);
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET updated_at = $2,
                data = jsonb_set(
                    jsonb_set(data, '{updatedAt}', $3::jsonb),
                    '{messages}',
                    (data->'messages') || $4::jsonb
                )
            WHERE session_id = $1
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue(msgJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateTitleAsync(Guid sessionId, string title, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET updated_at = $2,
                data = jsonb_set(
                    jsonb_set(data, '{updatedAt}', $3::jsonb),
                    '{title}', $4::jsonb
                )
            WHERE session_id = $1
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(title));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateRollingSummaryAsync(Guid sessionId, string summary, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET updated_at = $2,
                data = jsonb_set(
                    jsonb_set(data, '{updatedAt}', $3::jsonb),
                    '{rollingSummary}', $4::jsonb
                )
            WHERE session_id = $1
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(summary));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(Guid sessionId, ChatSessionStatus status, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET status = $2,
                updated_at = $3,
                data = jsonb_set(
                    jsonb_set(data, '{updatedAt}', $4::jsonb),
                    '{status}', $5::jsonb
                )
            WHERE session_id = $1
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(status.ToString());
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.UtcDateTime);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(status.ToString()));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<long> CompleteIdleSessionsAsync(DateTimeOffset idleBefore, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            UPDATE {TableName}
            SET status = 'Completed', data = data || $2::jsonb
            WHERE status = 'Active' AND updated_at < $1
            """);
        cmd.Parameters.AddWithValue(idleBefore.UtcDateTime);
        cmd.Parameters.AddWithValue(CompletePatch());

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<long> CompleteOtherActiveSessionsAsync(string? userId, Guid exceptSessionId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            UPDATE {TableName}
            SET status = 'Completed', data = data || $3::jsonb
            WHERE status = 'Active'
              AND user_id IS NOT DISTINCT FROM $1
              AND session_id <> $2
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(exceptSessionId.ToString());
        cmd.Parameters.AddWithValue(CompletePatch());

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The JSONB members a completion sets, merged into the document with <c>||</c>.</summary>
    private static string CompletePatch() => JsonSerializer.Serialize(new
    {
        status = ChatSessionStatus.Completed,
        embeddingStatus = ChatSessionEmbeddingStatus.Pending,
        completedAt = DateTimeOffset.UtcNow,
    }, SerializerOptions);

    /// <inheritdoc/>
    public async Task ReactivateAsync(Guid sessionId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            UPDATE {TableName}
            SET status = 'Active', data = data || $2::jsonb
            WHERE session_id = $1
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(new
        {
            status = ChatSessionStatus.Active,
            embeddingStatus = ChatSessionEmbeddingStatus.None,
            summary = (string?)null,
        }, SerializerOptions));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<ChatSession>> GetCompletedByEmbeddingStatusAsync(
        IEnumerable<ChatSessionEmbeddingStatus> statuses, int maxCount,
        IReadOnlyCollection<Guid>? excludeIds = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var hasExclusions = excludeIds is { Count: > 0 };

        // Sessions stored before embedding status existed have no member; they read as None.
        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE status = 'Completed'
              AND COALESCE(data->>'embeddingStatus', 'None') = ANY($1)
              {(hasExclusions ? "AND session_id <> ALL($3)" : string.Empty)}
            ORDER BY updated_at
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue(statuses.Select(st => st.ToString()).ToArray());
        cmd.Parameters.AddWithValue(maxCount);
        if (hasExclusions)
            cmd.Parameters.AddWithValue(excludeIds!.Select(id => id.ToString()).ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChatSession>();

        while (await reader.ReadAsync(ct))
        {
            var session = JsonSerializer.Deserialize<ChatSession>(reader.GetString(0), SerializerOptions);
            if (session is not null) results.Add(session);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateMemoryStateAsync(
        Guid sessionId, string? summary, ChatSessionEmbeddingStatus status, int expectedMessageCount,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            UPDATE {TableName}
            SET data = data || $2::jsonb
            WHERE session_id = $1
              AND status = 'Completed'
              AND jsonb_array_length(data->'messages') = $3
            """);
        cmd.Parameters.AddWithValue(sessionId.ToString());
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(new { summary, embeddingStatus = status }, SerializerOptions));
        cmd.Parameters.AddWithValue(expectedMessageCount);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <inheritdoc/>
    public async Task<List<ChatSession>> ListRecentAsync(int limit = 50, string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        // Exclude messages and the summaries to minimise payload for list view.
        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data - 'messages' - 'rollingSummary' - 'summary'
            FROM {TableName}
            WHERE user_id IS NOT DISTINCT FROM $1
            ORDER BY updated_at DESC
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChatSession>();

        while (await reader.ReadAsync(ct))
        {
            var session = JsonSerializer.Deserialize<ChatSession>(reader.GetString(0), SerializerOptions);
            if (session is not null) results.Add(session);
        }

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
                    session_id  TEXT PRIMARY KEY,
                    status      TEXT NOT NULL DEFAULT 'Active',
                    user_id     TEXT,
                    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    data        JSONB NOT NULL
                );

                ALTER TABLE {TableName} ADD COLUMN IF NOT EXISTS user_id TEXT;

                CREATE INDEX IF NOT EXISTS {TableName}_updated_at_idx
                    ON {TableName} (updated_at DESC);

                CREATE INDEX IF NOT EXISTS {TableName}_status_idx
                    ON {TableName} (status);

                CREATE INDEX IF NOT EXISTS {TableName}_user_id_idx
                    ON {TableName} (user_id);
                """);

            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Ensured Postgres chat_sessions table schema.");
            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
