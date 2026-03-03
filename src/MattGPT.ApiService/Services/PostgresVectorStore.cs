using System.Globalization;
using MattGPT.ApiService.Models;
using Npgsql;

namespace MattGPT.ApiService.Services;

/// <summary>
/// PostgreSQL/pgvector-backed implementation of <see cref="IVectorStore"/>.
/// Uses the <c>pgvector</c> extension for efficient cosine similarity search.
/// Vectors are sent as text in pgvector notation (e.g. <c>[1.0, 2.0, 3.0]</c>) and cast to the
/// <c>vector</c> type in SQL, requiring no additional .NET type-mapping packages.
/// The schema is created automatically on first use.
/// </summary>
public class PostgresVectorStore(NpgsqlDataSource dataSource, ILogger<PostgresVectorStore> logger) : IVectorStore
{
    private const string TableName = "conversation_vectors";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc/>
    public async Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(vector.Length, ct);

        var embeddingText = FormatVector(vector);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            INSERT INTO {{TableName}}
                (conversation_id, title, summary, create_time, update_time, user_id, embedding)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7::vector)
            ON CONFLICT (conversation_id) DO UPDATE SET
                title       = EXCLUDED.title,
                summary     = EXCLUDED.summary,
                create_time = EXCLUDED.create_time,
                update_time = EXCLUDED.update_time,
                user_id     = EXCLUDED.user_id,
                embedding   = EXCLUDED.embedding
            """);

        cmd.Parameters.AddWithValue(conversation.ConversationId);
        cmd.Parameters.AddWithValue((object?)conversation.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.CreateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.UpdateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)conversation.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(embeddingText);

        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Postgres vector store.",
            conversation.ConversationId, conversation.Title);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
    {
        if (!await TableExistsAsync(ct))
            return [];

        var queryText = FormatVector(queryVector);

        await using var cmd = dataSource.CreateCommand(
            $$"""
            SELECT conversation_id, title, summary,
                   1 - (embedding <=> $1::vector) AS score
            FROM {{TableName}}
            WHERE user_id IS NOT DISTINCT FROM $2
            ORDER BY embedding <=> $1::vector
            LIMIT $3
            """);

        cmd.Parameters.AddWithValue(queryText);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<VectorSearchResult>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(new VectorSearchResult(
                ConversationId: reader.GetString(0),
                Score: (float)reader.GetDouble(3),
                Title: reader.IsDBNull(1) ? null : reader.GetString(1),
                Summary: reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
    {
        if (!await TableExistsAsync(ct))
            return null;

        await using var cmd = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {TableName}");

        var count = await cmd.ExecuteScalarAsync(ct);
        return count is long l ? (ulong)l : null;
    }

    private async Task<bool> TableExistsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = $1)");
        cmd.Parameters.AddWithValue(TableName);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Ensures the pgvector extension and conversations vector table exist.
    /// On first call the schema is created with the given <paramref name="dimensions"/>.
    /// If the table already exists its vector column dimension is validated; a mismatch
    /// throws <see cref="InvalidOperationException"/> with a clear diagnostic message.
    /// Subsequent calls are no-ops once the schema has been verified.
    /// </summary>
    private async Task EnsureSchemaAsync(int dimensions, CancellationToken ct)
    {
        if (_schemaEnsured) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;

            // Enable the extension before touching the table.
            await using var extCmd = dataSource.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector");
            await extCmd.ExecuteNonQueryAsync(ct);

            // Check whether the table already exists and, if so, validate the stored dimension.
            await using var dimCmd = dataSource.CreateCommand(
                """
                SELECT atttypmod
                FROM   pg_attribute
                JOIN   pg_class ON pg_class.oid = pg_attribute.attrelid
                WHERE  pg_class.relname = $1
                AND    attname = 'embedding'
                AND    attnum > 0
                """);
            dimCmd.Parameters.AddWithValue(TableName);
            var existing = await dimCmd.ExecuteScalarAsync(ct);

            if (existing is int storedDim && storedDim > 0 && storedDim != dimensions)
            {
                throw new InvalidOperationException(
                    $"Postgres vector store dimension mismatch: table '{TableName}' was created " +
                    $"with {storedDim} dimensions but the current embedding model produces {dimensions}. " +
                    "Re-embed all conversations via POST /conversations/embed after changing the embedding model.");
            }

            await using var cmd = dataSource.CreateCommand(
                $$"""
                CREATE TABLE IF NOT EXISTS {{TableName}} (
                    conversation_id TEXT PRIMARY KEY,
                    title           TEXT,
                    summary         TEXT,
                    create_time     DOUBLE PRECISION,
                    update_time     DOUBLE PRECISION,
                    user_id         TEXT,
                    embedding       vector({{dimensions}})
                );

                CREATE INDEX IF NOT EXISTS {{TableName}}_embedding_idx
                    ON {{TableName}} USING hnsw (embedding vector_cosine_ops);

                CREATE INDEX IF NOT EXISTS conversation_vectors_user_id_idx
                    ON {{TableName}} (user_id);
                """);

            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation(
                "Ensured Postgres vector store schema with {Dims} dimensions.",
                dimensions);

            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Formats a float array as a pgvector literal, e.g. <c>[1.0, 2.0, 3.0]</c>.</summary>
    private static string FormatVector(float[] vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString("G", CultureInfo.InvariantCulture))) + "]";
}
