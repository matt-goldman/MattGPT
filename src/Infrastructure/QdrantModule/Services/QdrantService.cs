using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MattGPT.QdrantModule.Services;

/// <summary>
/// Qdrant-backed implementation of <see cref="IVectorStore"/>.
/// Ensures the collection is created on first use and stores one point per conversation chunk,
/// keyed by a deterministic UUID derived from the conversation id and the chunk ordinal.
/// </summary>
/// <remarks>
/// Qdrant returns raw cosine similarity as the score, so values are directly comparable within this
/// store and not with any other store's scores.
/// </remarks>
public class QdrantVectorStore(QdrantClient client, ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private const string CollectionName = "conversations";
    private volatile bool _collectionEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc/>
    public async Task UpsertAsync(
        StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0)
        {
            await DeleteAsync(conversation.ConversationId, ct);
            return;
        }

        await EnsureCollectionAsync((ulong)chunks[0].Vector.Length, ct);

        // Replace, not merge: a conversation re-embedded into fewer chunks (or under another strategy)
        // must not leave the previous points behind to be matched against.
        await DeleteAsync(conversation.ConversationId, ct);

        var points = chunks.Select(c => new PointStruct
        {
            Id = new PointId { Uuid = ChunkPointId.Uuid(conversation.ConversationId, c.Chunk.Ordinal).ToString("D") },
            Vectors = c.Vector,
            Payload =
            {
                ["conversation_id"] = conversation.ConversationId,
                ["title"] = conversation.Title ?? string.Empty,
                ["summary"] = conversation.Summary ?? string.Empty,
                ["create_time"] = conversation.CreateTime ?? 0.0,
                ["update_time"] = conversation.UpdateTime ?? 0.0,
                ["default_model_slug"] = conversation.DefaultModelSlug ?? string.Empty,
                ["gizmo_id"] = conversation.GizmoId ?? string.Empty,
                ["is_archived"] = conversation.IsArchived ?? false,
                ["user_id"] = conversation.UserId ?? string.Empty,
                ["chunk_ordinal"] = c.Chunk.Ordinal,
                ["chunk_count"] = chunks.Count,
                ["chunk_start_message"] = c.Chunk.StartMessageIndex,
                ["chunk_end_message"] = c.Chunk.EndMessageIndex,
                ["chunk_strategy"] = c.Chunk.Strategy.ToString(),
            }
        }).ToList();

        await client.UpsertAsync(CollectionName, points, cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Qdrant as {Chunks} chunk(s).",
            conversation.ConversationId, conversation.Title, points.Count);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        if (!await client.CollectionExistsAsync(CollectionName, ct))
            return;

        await client.DeleteAsync(CollectionName, ConversationFilter(conversationId), cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
    {
        if (!await client.CollectionExistsAsync(CollectionName, ct))
            return [];

        Filter? filter = userId is not null
            ? new Filter
            {
                Must =
                {
                    new Condition { Field = new FieldCondition
                    {
                        Key = "user_id",
                        Match = new Match { Text = userId }
                    }}
                }
            }
            : null;

        var results = await client.SearchAsync(
            CollectionName,
            queryVector,
            filter: filter,
            limit: (ulong)limit,
            cancellationToken: ct);

        return [.. results.Select(r => new VectorSearchResult(
            ConversationId: GetPayloadString(r.Payload, "conversation_id") ?? string.Empty,
            Score: r.Score,
            Title: GetPayloadString(r.Payload, "title"),
            Summary: GetPayloadString(r.Payload, "summary"),
            Chunk: ReadChunk(r.Payload)
        ))];
    }

    /// <summary>A filter matching every point of one conversation.</summary>
    private static Filter ConversationFilter(string conversationId) => new()
    {
        Must =
        {
            new Condition { Field = new FieldCondition
            {
                Key = "conversation_id",
                Match = new Match { Keyword = conversationId }
            }}
        }
    };

    /// <summary>
    /// The chunk address stored with a point, or null for a point written before chunking existed
    /// (which is treated as the whole conversation).
    /// </summary>
    private static ChunkHit? ReadChunk(IReadOnlyDictionary<string, Value> payload)
    {
        if (GetPayloadInt(payload, "chunk_ordinal") is not { } ordinal)
            return null;

        var strategy = Enum.TryParse<ChunkingStrategy>(GetPayloadString(payload, "chunk_strategy"), out var parsed)
            ? parsed
            : ChunkingStrategy.WholeConversation;

        return new ChunkHit(
            Ordinal:           (int)ordinal,
            ChunkCount:        (int)(GetPayloadInt(payload, "chunk_count") ?? 1),
            StartMessageIndex: (int)(GetPayloadInt(payload, "chunk_start_message") ?? 0),
            EndMessageIndex:   (int)(GetPayloadInt(payload, "chunk_end_message") ?? -1),
            Strategy:          strategy);
    }

    private static string? GetPayloadString(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;
    }

    private static long? GetPayloadInt(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.IntegerValue
            ? value.IntegerValue
            : null;
    }

    /// <inheritdoc/>
    public async Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
    {
        if (!await client.CollectionExistsAsync(CollectionName, ct))
            return null;

        var info = await client.GetCollectionInfoAsync(CollectionName, ct);
        return info.PointsCount;
    }

    /// <summary>
    /// Ensures the Qdrant collection exists with the correct vector dimensions.
    /// Called lazily on the first upsert; subsequent calls are no-ops.
    /// </summary>
    private async Task EnsureCollectionAsync(ulong dimensions, CancellationToken ct)
    {
        if (_collectionEnsured) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_collectionEnsured) return;

            if (!await client.CollectionExistsAsync(CollectionName, ct))
            {
                await client.CreateCollectionAsync(
                    CollectionName,
                    new VectorParams { Size = dimensions, Distance = Distance.Cosine },
                    cancellationToken: ct);

                logger.LogInformation(
                    "Created Qdrant collection '{Collection}' with {Dims} dimensions.",
                    CollectionName, dimensions);
            }

            _collectionEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
