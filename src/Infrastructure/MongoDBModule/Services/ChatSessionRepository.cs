using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MongoDB.Driver;

namespace MattGPT.MongoDBModule.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="IChatSessionRepository"/>.
/// Stores chat sessions in a dedicated <c>chat_sessions</c> collection.
/// </summary>
public class ChatSessionRepository : IChatSessionRepository
{
    private readonly IMongoCollection<ChatSession> _collection;

    public ChatSessionRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase("mattgptdb");
        _collection = db.GetCollection<ChatSession>("chat_sessions");
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        var keys = Builders<ChatSession>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<ChatSession>(keys.Descending(x => x.CreatedAt)),
            new CreateIndexModel<ChatSession>(keys.Ascending(x => x.Status)),
            new CreateIndexModel<ChatSession>(keys.Descending(x => x.UpdatedAt)),
            new CreateIndexModel<ChatSession>(keys.Ascending(x => x.UserId)),
            new CreateIndexModel<ChatSession>(keys.Ascending(x => x.Status).Ascending(x => x.EmbeddingStatus)),
        ]);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(ChatSession session, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(session, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<ChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc/>
    public async Task AddMessageAsync(Guid sessionId, ChatSessionMessage message, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        var update = Builders<ChatSession>.Update
            .Push(x => x.Messages, message)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task UpdateTitleAsync(Guid sessionId, string title, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        var update = Builders<ChatSession>.Update
            .Set(x => x.Title, title)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task UpdateRollingSummaryAsync(Guid sessionId, string summary, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        var update = Builders<ChatSession>.Update
            .Set(x => x.RollingSummary, summary)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(Guid sessionId, ChatSessionStatus status, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        var update = Builders<ChatSession>.Update
            .Set(x => x.Status, status)
            .Set(x => x.UpdatedAt, DateTimeOffset.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<long> CompleteIdleSessionsAsync(DateTimeOffset idleBefore, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.And(
            Builders<ChatSession>.Filter.Eq(x => x.Status, ChatSessionStatus.Active),
            Builders<ChatSession>.Filter.Lt(x => x.UpdatedAt, idleBefore));
        var result = await _collection.UpdateManyAsync(filter, CompleteUpdate(), cancellationToken: ct);
        return result.ModifiedCount;
    }

    /// <inheritdoc/>
    public async Task<long> CompleteOtherActiveSessionsAsync(string? userId, Guid exceptSessionId, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.And(
            Builders<ChatSession>.Filter.Eq(x => x.Status, ChatSessionStatus.Active),
            Builders<ChatSession>.Filter.Eq(x => x.UserId, userId),
            Builders<ChatSession>.Filter.Ne(x => x.SessionId, exceptSessionId));
        var result = await _collection.UpdateManyAsync(filter, CompleteUpdate(), cancellationToken: ct);
        return result.ModifiedCount;
    }

    private static UpdateDefinition<ChatSession> CompleteUpdate() => Builders<ChatSession>.Update
        .Set(x => x.Status, ChatSessionStatus.Completed)
        .Set(x => x.EmbeddingStatus, ChatSessionEmbeddingStatus.Pending)
        .Set(x => x.CompletedAt, DateTimeOffset.UtcNow);

    /// <inheritdoc/>
    public async Task ReactivateAsync(Guid sessionId, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId);
        var update = Builders<ChatSession>.Update
            .Set(x => x.Status, ChatSessionStatus.Active)
            .Set(x => x.Summary, null)
            .Set(x => x.EmbeddingStatus, ChatSessionEmbeddingStatus.None);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<List<ChatSession>> GetCompletedByEmbeddingStatusAsync(
        IEnumerable<ChatSessionEmbeddingStatus> statuses, int maxCount,
        IReadOnlyCollection<Guid>? excludeIds = null, CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.And(
            Builders<ChatSession>.Filter.Eq(x => x.Status, ChatSessionStatus.Completed),
            Builders<ChatSession>.Filter.In(x => x.EmbeddingStatus, statuses));
        if (excludeIds is { Count: > 0 })
            filter &= Builders<ChatSession>.Filter.Nin(x => x.SessionId, excludeIds);

        return await _collection
            .Find(filter)
            .SortBy(x => x.CompletedAt)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateMemoryStateAsync(
        Guid sessionId, string? summary, ChatSessionEmbeddingStatus status, int expectedMessageCount,
        CancellationToken ct = default)
    {
        var filter = Builders<ChatSession>.Filter.And(
            Builders<ChatSession>.Filter.Eq(x => x.SessionId, sessionId),
            Builders<ChatSession>.Filter.Eq(x => x.Status, ChatSessionStatus.Completed),
            Builders<ChatSession>.Filter.Size(x => x.Messages, expectedMessageCount));
        var update = Builders<ChatSession>.Update
            .Set(x => x.Summary, summary)
            .Set(x => x.EmbeddingStatus, status);
        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    /// <inheritdoc/>
    public async Task<List<ChatSession>> ListRecentAsync(int limit = 50, string? userId = null, CancellationToken ct = default)
    {
        // Exclude Messages and the summaries to minimise payload for the list view.
        // The caller only needs SessionId, Title, CreatedAt, UpdatedAt, and Status.
        var projection = Builders<ChatSession>.Projection
            .Exclude(x => x.Messages)
            .Exclude(x => x.RollingSummary)
            .Exclude(x => x.Summary);

        return await _collection
            .Find(Builders<ChatSession>.Filter.Eq(x => x.UserId, userId))
            .Project<ChatSession>(projection)
            .SortByDescending(x => x.UpdatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }
}
