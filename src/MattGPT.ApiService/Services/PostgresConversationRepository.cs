using System.Text.Json;
using MattGPT.ApiService.Models;
using Npgsql;

namespace MattGPT.ApiService.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IConversationRepository"/>.
/// Stores conversations as JSONB documents in a <c>conversations</c> table, with
/// key scalar fields promoted to indexed columns for efficient querying.
/// </summary>
public class PostgresConversationRepository(NpgsqlDataSource dataSource, ILogger<PostgresConversationRepository> logger)
    : IConversationRepository
{
    private const string TableName = "conversations";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc/>
    public async Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var data = JsonSerializer.Serialize(conversation, SerializerOptions);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName}
                (conversation_id, processing_status, create_time, update_time,
                 gizmo_type, conversation_template_id, user_id, data)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8::jsonb)
            ON CONFLICT (conversation_id) DO UPDATE SET
                processing_status        = EXCLUDED.processing_status,
                create_time              = EXCLUDED.create_time,
                update_time              = EXCLUDED.update_time,
                gizmo_type               = EXCLUDED.gizmo_type,
                conversation_template_id = EXCLUDED.conversation_template_id,
                user_id                  = EXCLUDED.user_id,
                data                     = EXCLUDED.data
            """);

        cmd.Parameters.AddWithValue(conversation.ConversationId);
        cmd.Parameters.AddWithValue(conversation.ProcessingStatus.ToString());
        cmd.Parameters.AddWithValue((object?)conversation.CreateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.UpdateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.GizmoType ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.ConversationTemplateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(data);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetPageAsync(
        int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var countCmd = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {TableName} WHERE user_id IS NOT DISTINCT FROM $1");
        countCmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        var total = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE user_id IS NOT DISTINCT FROM $1
            ORDER BY update_time DESC NULLS LAST
            LIMIT $2 OFFSET $3
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(pageSize);
        cmd.Parameters.AddWithValue((page - 1) * pageSize);

        return (await ReadConversationsAsync(cmd, excludeEmbedding: true, ct), total);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByStatusAsync(
        ConversationProcessingStatus status, int maxCount, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE processing_status = $1
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue(status.ToString());
        cmd.Parameters.AddWithValue(maxCount);

        return await ReadConversationsAsync(cmd, excludeEmbedding: false, ct);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByStatusesAsync(
        IEnumerable<ConversationProcessingStatus> statuses, int maxCount, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var statusStrings = statuses.Select(s => s.ToString()).ToArray();

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE processing_status = ANY($1)
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue(statusStrings);
        cmd.Parameters.AddWithValue(maxCount);

        return await ReadConversationsAsync(cmd, excludeEmbedding: false, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateSummaryAsync(
        string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET processing_status = $2,
                data = jsonb_set(jsonb_set(data, '{summary}', $3::jsonb), '{processingStatus}', $4::jsonb)
            WHERE conversation_id = $1
            """);
        cmd.Parameters.AddWithValue(conversationId);
        cmd.Parameters.AddWithValue(status.ToString());
        cmd.Parameters.AddWithValue(summary is null ? "null" : JsonSerializer.Serialize(summary));
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(status.ToString()));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateEmbeddingAsync(
        string conversationId, float[]? embedding, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            UPDATE {{TableName}}
            SET processing_status = $2,
                data = jsonb_set(jsonb_set(data, '{embedding}', $3::jsonb), '{processingStatus}', $4::jsonb)
            WHERE conversation_id = $1
            """);
        cmd.Parameters.AddWithValue(conversationId);
        cmd.Parameters.AddWithValue(status.ToString());
        cmd.Parameters.AddWithValue(embedding is null ? "null" : JsonSerializer.Serialize(embedding));
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(status.ToString()));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<StoredConversation?> GetByIdAsync(string conversationId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT data FROM {TableName} WHERE conversation_id = $1");
        cmd.Parameters.AddWithValue(conversationId);

        var rows = await ReadConversationsAsync(cmd, excludeEmbedding: true, ct);
        return rows.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByIdsAsync(
        IEnumerable<string> conversationIds, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        var ids = conversationIds.ToArray();

        await using var cmd = dataSource.CreateCommand(
            $"SELECT data FROM {TableName} WHERE conversation_id = ANY($1)");
        cmd.Parameters.AddWithValue(ids);

        return await ReadConversationsAsync(cmd, excludeEmbedding: false, ct);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT processing_status, COUNT(*) FROM {TableName} WHERE user_id IS NOT DISTINCT FROM $1 GROUP BY processing_status");
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var counts = new Dictionary<ConversationProcessingStatus, long>();

        while (await reader.ReadAsync(ct))
        {
            if (Enum.TryParse<ConversationProcessingStatus>(reader.GetString(0), out var status))
                counts[status] = reader.GetInt64(1);
        }

        // Ensure all statuses are present.
        foreach (var status in Enum.GetValues<ConversationProcessingStatus>())
            counts.TryAdd(status, 0);

        return counts;
    }

    /// <inheritdoc/>
    public async Task<List<ConversationProject>> GetProjectsAsync(string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        // Single query using DISTINCT ON to get both the aggregate stats and the most recent
        // title in one pass, avoiding an N+1 round trip per project.
        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT
                agg.conversation_template_id,
                agg.conversation_count,
                agg.latest_update_time,
                agg.earliest_create_time,
                recent.title
            FROM (
                SELECT
                    conversation_template_id,
                    COUNT(*)    AS conversation_count,
                    MAX(update_time) AS latest_update_time,
                    MIN(create_time) AS earliest_create_time
                FROM {TableName}
                WHERE gizmo_type = 'snorlax'
                  AND conversation_template_id IS NOT NULL
                  AND user_id IS NOT DISTINCT FROM $1
                GROUP BY conversation_template_id
            ) agg
            JOIN LATERAL (
                SELECT data->>'title' AS title
                FROM {TableName}
                WHERE gizmo_type = 'snorlax'
                  AND conversation_template_id = agg.conversation_template_id
                  AND user_id IS NOT DISTINCT FROM $1
                ORDER BY update_time DESC NULLS LAST
                LIMIT 1
            ) recent ON true
            ORDER BY agg.latest_update_time DESC NULLS LAST
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var projects = new List<ConversationProject>();

        while (await reader.ReadAsync(ct))
        {
            projects.Add(new ConversationProject
            {
                TemplateId = reader.GetString(0),
                ConversationCount = (int)reader.GetInt64(1),
                LatestUpdateTime = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                EarliestCreateTime = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                MostRecentTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return projects;
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetProjectConversationsAsync(
        string templateId, int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var countCmd = dataSource.CreateCommand(
            $"""
            SELECT COUNT(*) FROM {TableName}
            WHERE gizmo_type = 'snorlax' AND conversation_template_id = $1
              AND user_id IS NOT DISTINCT FROM $2
            """);
        countCmd.Parameters.AddWithValue(templateId);
        countCmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        var total = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE gizmo_type = 'snorlax' AND conversation_template_id = $1
              AND user_id IS NOT DISTINCT FROM $2
            ORDER BY update_time DESC NULLS LAST
            LIMIT $3 OFFSET $4
            """);
        cmd.Parameters.AddWithValue(templateId);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(pageSize);
        cmd.Parameters.AddWithValue((page - 1) * pageSize);

        return (await ReadConversationsAsync(cmd, excludeEmbedding: true, ct), total);
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetNonProjectConversationsAsync(
        int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var countCmd = dataSource.CreateCommand(
            $"""
            SELECT COUNT(*) FROM {TableName}
            WHERE (gizmo_type IS DISTINCT FROM 'snorlax'
               OR conversation_template_id IS NULL)
              AND user_id IS NOT DISTINCT FROM $1
            """);
        countCmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        var total = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT data FROM {TableName}
            WHERE (gizmo_type IS DISTINCT FROM 'snorlax'
               OR conversation_template_id IS NULL)
              AND user_id IS NOT DISTINCT FROM $1
            ORDER BY update_time DESC NULLS LAST
            LIMIT $2 OFFSET $3
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(pageSize);
        cmd.Parameters.AddWithValue((page - 1) * pageSize);

        return (await ReadConversationsAsync(cmd, excludeEmbedding: true, ct), total);
    }

    private static async Task<List<StoredConversation>> ReadConversationsAsync(
        NpgsqlCommand cmd, bool excludeEmbedding, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<StoredConversation>();

        while (await reader.ReadAsync(ct))
        {
            var json = reader.GetString(0);
            var conv = JsonSerializer.Deserialize<StoredConversation>(json, SerializerOptions);
            if (conv is null) continue;

            if (excludeEmbedding)
                conv.Embedding = null;

            results.Add(conv);
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
                    conversation_id          TEXT PRIMARY KEY,
                    processing_status        TEXT NOT NULL DEFAULT 'Imported',
                    create_time              DOUBLE PRECISION,
                    update_time              DOUBLE PRECISION,
                    gizmo_type               TEXT,
                    conversation_template_id TEXT,
                    user_id                  TEXT,
                    data                     JSONB NOT NULL
                );

                CREATE INDEX IF NOT EXISTS {TableName}_processing_status_idx
                    ON {TableName} (processing_status);

                CREATE INDEX IF NOT EXISTS {TableName}_update_time_idx
                    ON {TableName} (update_time DESC NULLS LAST);

                CREATE INDEX IF NOT EXISTS {TableName}_create_time_idx
                    ON {TableName} (create_time);

                CREATE INDEX IF NOT EXISTS {TableName}_gizmo_type_idx
                    ON {TableName} (gizmo_type);

                CREATE INDEX IF NOT EXISTS {TableName}_template_id_idx
                    ON {TableName} (conversation_template_id);

                CREATE INDEX IF NOT EXISTS {TableName}_user_id_idx
                    ON {TableName} (user_id);
                """);

            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Ensured Postgres conversations table schema.");
            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
