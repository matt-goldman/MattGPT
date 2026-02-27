using MattGPT.ApiService.Models;
using MongoDB.Driver;

namespace MattGPT.ApiService.Services;

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
    public async Task<List<ChatSession>> ListRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        // Exclude Messages and RollingSummary to minimise payload for the list view.
        // The caller only needs SessionId, Title, CreatedAt, UpdatedAt, and Status.
        var projection = Builders<ChatSession>.Projection
            .Exclude(x => x.Messages)
            .Exclude(x => x.RollingSummary);

        return await _collection
            .Find(Builders<ChatSession>.Filter.Empty)
            .Project<ChatSession>(projection)
            .SortByDescending(x => x.UpdatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }
}
