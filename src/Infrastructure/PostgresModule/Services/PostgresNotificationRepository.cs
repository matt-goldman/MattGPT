using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MattGPT.PostgresModule.Services;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="INotificationRepository"/>. Notifications are small
/// and flat, so they are plain columns rather than a JSONB document.
/// </summary>
public class PostgresNotificationRepository(NpgsqlDataSource dataSource, ILogger<PostgresNotificationRepository> logger)
    : INotificationRepository
{
    private const string TableName = "notifications";
    private volatile bool _schemaEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc/>
    public async Task CreateAsync(Notification notification, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            INSERT INTO {TableName} (id, user_id, kind, title, message, link, created_at, is_read)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """);
        cmd.Parameters.AddWithValue(notification.Id);
        cmd.Parameters.AddWithValue((object?)notification.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(notification.Kind.ToString());
        cmd.Parameters.AddWithValue(notification.Title);
        cmd.Parameters.AddWithValue(notification.Message);
        cmd.Parameters.AddWithValue((object?)notification.Link ?? DBNull.Value);
        cmd.Parameters.AddWithValue(notification.CreatedAt.UtcDateTime);
        cmd.Parameters.AddWithValue(notification.IsRead);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<Notification>> ListRecentAsync(string? userId, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            SELECT id, user_id, kind, title, message, link, created_at, is_read
            FROM {TableName}
            WHERE user_id IS NOT DISTINCT FROM $1
            ORDER BY created_at DESC
            LIMIT $2
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Notification>();
        while (await reader.ReadAsync(ct))
        {
            if (!Enum.TryParse<NotificationKind>(reader.GetString(2), out var kind))
                continue;

            results.Add(new Notification
            {
                Id = reader.GetString(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = kind,
                Title = reader.GetString(3),
                Message = reader.GetString(4),
                Link = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero),
                IsRead = reader.GetBoolean(7),
            });
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<long> CountUnreadAsync(string? userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"SELECT COUNT(*) FROM {TableName} WHERE user_id IS NOT DISTINCT FROM $1 AND NOT is_read");
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);

        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <inheritdoc/>
    public async Task MarkReadAsync(string? userId, IReadOnlyCollection<string>? ids, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var cmd = dataSource.CreateCommand(
            $"""
            UPDATE {TableName} SET is_read = TRUE
            WHERE user_id IS NOT DISTINCT FROM $1 AND NOT is_read
            {(ids is null ? string.Empty : "AND id = ANY($2)")}
            """);
        cmd.Parameters.AddWithValue((object?)userId ?? DBNull.Value);
        if (ids is not null)
            cmd.Parameters.AddWithValue(ids.ToArray());

        await cmd.ExecuteNonQueryAsync(ct);
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
                    id          TEXT PRIMARY KEY,
                    user_id     TEXT,
                    kind        TEXT NOT NULL,
                    title       TEXT NOT NULL,
                    message     TEXT NOT NULL,
                    link        TEXT,
                    created_at  TIMESTAMPTZ NOT NULL,
                    is_read     BOOLEAN NOT NULL DEFAULT FALSE
                );

                CREATE INDEX IF NOT EXISTS {TableName}_user_created_idx
                    ON {TableName} (user_id, created_at DESC);
                """);

            await cmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Ensured Postgres notifications table schema.");
            _schemaEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
