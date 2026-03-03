using MattGPT.ApiService.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MattGPT.ApiService.Services;

/// <summary>A provider-neutral vector search result.</summary>
public record VectorSearchResult(string ConversationId, float Score, string? Title, string? Summary);

/// <summary>Provider-agnostic abstraction for vector storage and similarity search.</summary>
public interface IVectorStore
{
    /// <summary>Upsert a conversation embedding with its metadata payload.</summary>
    Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default);

    /// <summary>Search for the most similar conversations to the given query vector, optionally scoped to a user.</summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default);

    /// <summary>Return the number of points in the conversations collection, or null if the collection doesn't exist.</summary>
    Task<ulong?> GetPointCountAsync(CancellationToken ct = default);
}

/// <summary>
/// Qdrant-backed implementation of <see cref="IVectorStore"/>.
/// Ensures the collection is created on first use and upserts points idempotently using
/// the conversation UUID as the point ID.
/// </summary>
public class QdrantVectorStore(QdrantClient client, ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private const string CollectionName = "conversations";
    private volatile bool _collectionEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <inheritdoc/>
    public async Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
    {
        await EnsureCollectionAsync((ulong)vector.Length, ct);

        var point = new PointStruct
        {
            Id = new PointId { Uuid = conversation.ConversationId },
            Vectors = vector,
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
            }
        };

        await client.UpsertAsync(CollectionName, [point], cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Qdrant.",
            conversation.ConversationId, conversation.Title);
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
            Summary: GetPayloadString(r.Payload, "summary")
        ))];
    }

    private static string? GetPayloadString(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
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
