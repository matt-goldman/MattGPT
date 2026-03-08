using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MongoDB.Driver;

namespace MattGPT.MongoDBModule.Services;

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
            new CreateIndexModel<StoredConversation>(keys.Descending(x => x.UpdateTime)),
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.ProcessingStatus)),
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.ConversationTemplateId)),
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.GizmoType)),
            new CreateIndexModel<StoredConversation>(keys.Ascending(x => x.UserId)),
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
        int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection
            .Find(filter)
            .SortByDescending(x => x.UpdateTime)
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
    public async Task<StoredConversation?> GetByIdAsync(string conversationId, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.Eq(x => x.ConversationId, conversationId);
        // Exclude the Embedding field — it's a large float[] not needed by UI consumers.
        var projection = Builders<StoredConversation>.Projection.Exclude(x => x.Embedding);
        return await _collection.Find(filter).Project<StoredConversation>(projection).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<StoredConversation>> GetByIdsAsync(
        IEnumerable<string> conversationIds, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.In(x => x.ConversationId, conversationIds);
        return await _collection.Find(filter).ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(string? userId = null, CancellationToken ct = default)
    {
        var counts = new Dictionary<ConversationProcessingStatus, long>();
        foreach (var status in Enum.GetValues<ConversationProcessingStatus>())
        {
            var filter = Builders<StoredConversation>.Filter.And(
                Builders<StoredConversation>.Filter.Eq(x => x.ProcessingStatus, status),
                Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId));
            counts[status] = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        }
        return counts;
    }

    /// <inheritdoc/>
    public async Task<List<ConversationProject>> GetProjectsAsync(string? userId = null, CancellationToken ct = default)
    {
        // Aggregate conversations where GizmoType is "snorlax" and ConversationTemplateId is set,
        // grouping by template ID to produce project summaries.
        var filter = Builders<StoredConversation>.Filter.And(
            Builders<StoredConversation>.Filter.Eq(x => x.GizmoType, "snorlax"),
            Builders<StoredConversation>.Filter.Ne(x => x.ConversationTemplateId, null),
            Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId));

        var pipeline = _collection.Aggregate()
            .Match(filter)
            .Group(
                x => x.ConversationTemplateId,
                g => new ConversationProject
                {
                    TemplateId = g.Key!,
                    ConversationCount = g.Count(),
                    MostRecentTitle = g.OrderByDescending(c => c.UpdateTime).First().Title,
                    LatestUpdateTime = g.Max(c => c.UpdateTime),
                    EarliestCreateTime = g.Min(c => c.CreateTime),
                })
            .SortByDescending(p => p.LatestUpdateTime);

        return await pipeline.ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetProjectConversationsAsync(
        string templateId, int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.And(
            Builders<StoredConversation>.Filter.Eq(x => x.GizmoType, "snorlax"),
            Builders<StoredConversation>.Filter.Eq(x => x.ConversationTemplateId, templateId),
            Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId));
        var projection = Builders<StoredConversation>.Projection.Exclude(x => x.Embedding);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection
            .Find(filter)
            .Project<StoredConversation>(projection)
            .SortByDescending(x => x.UpdateTime)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc/>
    public async Task<(List<StoredConversation> Items, long Total)> GetNonProjectConversationsAsync(
        int page, int pageSize, string? userId = null, CancellationToken ct = default)
    {
        // Conversations that don't belong to a project:
        // either GizmoType is not "snorlax" or ConversationTemplateId is null.
        var filter = Builders<StoredConversation>.Filter.And(
            Builders<StoredConversation>.Filter.Or(
                Builders<StoredConversation>.Filter.Ne(x => x.GizmoType, "snorlax"),
                Builders<StoredConversation>.Filter.Eq(x => x.ConversationTemplateId, null)),
            Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId));
        var projection = Builders<StoredConversation>.Projection.Exclude(x => x.Embedding);
        var total = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await _collection
            .Find(filter)
            .Project<StoredConversation>(projection)
            .SortByDescending(x => x.UpdateTime)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }
}
