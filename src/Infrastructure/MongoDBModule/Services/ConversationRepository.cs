using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MattGPT.MongoDBModule.Services;

/// <summary>
/// MongoDB-backed implementation of <see cref="IConversationRepository"/>.
/// Uses <c>ConversationId</c> as the document <c>_id</c> to support idempotent upserts.
/// </summary>
public class ConversationRepository : IConversationRepository
{
    /// <summary>Name of the compound text index backing <see cref="SearchTextAsync"/>.</summary>
    private const string TextIndexName = "conversation_text_idx";

    /// <summary>Projection field holding the per-document relevance rank from a $text query.</summary>
    private const string TextScoreField = "textScore";

    private readonly IMongoCollection<StoredConversation> _collection;
    private readonly ILogger<ConversationRepository> _logger;

    public ConversationRepository(IMongoClient mongoClient, ILogger<ConversationRepository> logger)
    {
        _logger = logger;
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

        CreateTextIndex();
    }

    /// <summary>
    /// Creates the text index used for keyword search, weighted so a hit in the title or
    /// summary outranks a passing mention in the message body.
    /// </summary>
    /// <remarks>
    /// Created separately from the other indexes, and failure-tolerant, for two reasons:
    /// a collection may only have ONE text index, so an index left over from a different
    /// field set makes creation fail with IndexOptionsConflict; and this index covers every
    /// message of every conversation, so the initial build on a large collection is the
    /// slowest part of startup. Neither should take the whole repository down - keyword
    /// search degrades to returning nothing while the rest of the app keeps working.
    /// </remarks>
    private void CreateTextIndex()
    {
        try
        {
            _collection.Indexes.CreateOne(new CreateIndexModel<StoredConversation>(
                Builders<StoredConversation>.IndexKeys
                    .Text(x => x.Title)
                    .Text(x => x.Summary)
                    .Text("LinearisedMessages.Parts"),
                new CreateIndexOptions
                {
                    Name = TextIndexName,
                    Weights = new BsonDocument
                    {
                        { "Title", 10 },
                        { "Summary", 5 },
                        { "LinearisedMessages.Parts", 1 },
                    },
                }));
        }
        catch (MongoCommandException ex)
        {
            _logger.LogWarning(ex,
                "Could not create the {IndexName} text index; keyword search will return no results. "
                + "Drop any pre-existing text index on the conversations collection and restart to fix this.",
                TextIndexName);
        }
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
        IEnumerable<ConversationProcessingStatus> statuses, int maxCount, IReadOnlyCollection<string>? excludeIds = null, CancellationToken ct = default)
    {
        var filter = Builders<StoredConversation>.Filter.In(x => x.ProcessingStatus, statuses);
        if (excludeIds is { Count: > 0 })
            filter &= Builders<StoredConversation>.Filter.Nin(x => x.ConversationId, excludeIds);

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
    public async Task<IReadOnlyList<ConversationTextSearchResult>> SearchTextAsync(
        string query, int maxResults, string? userId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return [];

        var filter = Builders<StoredConversation>.Filter.And(
            Builders<StoredConversation>.Filter.Text(query),
            Builders<StoredConversation>.Filter.Eq(x => x.UserId, userId));

        // A projection containing only $meta returns the whole document plus the computed
        // score, so the document still deserialises normally once the score is removed.
        var projection = Builders<StoredConversation>.Projection.MetaTextScore(TextScoreField);
        var sort = Builders<StoredConversation>.Sort.MetaTextScore(TextScoreField);

        List<BsonDocument> rows;
        try
        {
            rows = await _collection
                .Find(filter)
                .Project<BsonDocument>(projection)
                .Sort(sort)
                .Limit(maxResults)
                .ToListAsync(ct);
        }
        catch (MongoCommandException ex)
        {
            // Most likely the text index is missing (see CreateTextIndex). Callers treat
            // keyword search as best-effort, so degrade to "no matches" rather than throwing.
            _logger.LogWarning(ex, "Text search failed for query {Query}.", query);
            return [];
        }

        var results = new List<ConversationTextSearchResult>(rows.Count);

        foreach (var row in rows)
        {
            var score = row.TryGetValue(TextScoreField, out var scoreValue) ? scoreValue.ToDouble() : 0d;
            row.Remove(TextScoreField);

            var conversation = BsonSerializer.Deserialize<StoredConversation>(row);

            results.Add(new ConversationTextSearchResult(
                conversation.ConversationId,
                score,
                conversation.Title,
                conversation.Summary,
                ConversationTextSnippets.Build(conversation, query)));
        }

        return results;
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
