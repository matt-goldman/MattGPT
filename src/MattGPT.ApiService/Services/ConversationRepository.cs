using MattGPT.ApiService.Models;
using MongoDB.Driver;

namespace MattGPT.ApiService.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="IConversationRepository"/>.
/// Uses <c>ConversationId</c> as the document <c>_id</c> to support idempotent upserts.
/// </summary>
public class ConversationRepository : IConversationRepository
{
    private readonly IMongoCollection<StoredConversation> _collection;

    public ConversationRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase("mattgptdb");
        _collection = db.GetCollection<StoredConversation>("conversations");
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        var keys = Builders<StoredConversation>.IndexKeys;
        _collection.Indexes.CreateMany([
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.CreateTime)),
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.ProcessingStatus)),
        ]);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.ConversationId, conversation.ConversationId);
        await _collection.ReplaceOneAsync(filter, conversation, new ReplaceOptions { IsUpsert = true }, ct);
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetPageAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Empty;
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection
            .Find(filter)
            .SortByDescending(x => x.ImportTimestamp)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByStatusAsync(
        ConversationProcessingStatus status, int maxCount, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.ProcessingStatus, status);
        return await _collection
            .Find(filter)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByStatusesAsync(
        IEnumerable<ConversationProcessingStatus> statuses, int maxCount, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.In(x => x.ProcessingStatus, statuses);
        return await _collection
            .Find(filter)
            .Limit(maxCount)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task UpdateSummaryAsync(
        string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.ConversationId, conversationId);
        var update = Builders<StoredConversation>.Update
            .Set(x => x.Summary, summary)
            .Set(x => x.ProcessingStatus, status);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task UpdateEmbeddingAsync(
        string conversationId, float[]? embedding, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.ConversationId, conversationId);
        var update = Builders<StoredConversation>.Update
            .Set(x => x.Embedding, embedding)
            .Set(x => x.ProcessingStatus, status);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByIdsAsync(
        IEnumerable<string> conversationIds, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.In(x => x.ConversationId, conversationIds);
        return await _collection.Find(filter).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(CancellationToken ct = default)
    {
        var counts = new Dictionary<ConversationProcessingStatus, long>();
        foreach (var status in Enum.GetValues<ConversationProcessingStatus>())
        {
            var filter = Builders<StoredConversation>.Filter.Eq(x => x.ProcessingStatus, status);
            counts[status] = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        }
        return counts;
    }
}
