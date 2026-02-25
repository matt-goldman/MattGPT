using MattGPT.ApiService.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace MattGPT.ApiService.Services;

/// <summary>A single search result returned from Qdrant.</summary>
public record QdrantSearchResult(string ConversationId, float Score, string? Title, string? Summary);

/// <summary>Manages storage and similarity search of conversation embeddings in Qdrant.</summary>
/// <remarks>
/// TODO: Replace with a provider-agnostic <c>IVectorStore</c> abstraction before implementing
/// config-driven vector store provider selection (analogous to <see cref="LlmOptions"/>).
/// <c>QdrantService</c> would become one of several concrete implementations.
/// </remarks>
public interface IQdrantService
{
    /// <summary>Upsert a conversation embedding into Qdrant with its metadata payload.</summary>
    Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default);

    /// <summary>Search for the most similar conversations to the given query vector.</summary>
    Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(float[] queryVector, int limit = 5, CancellationToken ct = default);
}

/// <summary>
/// Qdrant-backed implementation of <see cref="IQdrantService"/>.
/// Ensures the collection is created on first use and upserts points idempotently using
/// the conversation UUID as the point ID.
/// </summary>
public class QdrantService : IQdrantService
{
    private const string CollectionName = "conversations";

    private readonly QdrantClient _client;
    private readonly ILogger<QdrantService> _logger;
    private volatile bool _collectionEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public QdrantService(QdrantClient client, ILogger<QdrantService> logger)
    {
        _client = client;
        _logger = logger;
    }

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
            }
        };

        await _client.UpsertAsync(CollectionName, [point], cancellationToken: ct);

        _logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Qdrant.",
            conversation.ConversationId, conversation.Title);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, CancellationToken ct = default)
    {
        if (!await _client.CollectionExistsAsync(CollectionName, ct))
            return [];

        var results = await _client.SearchAsync(
            CollectionName,
            queryVector,
            limit: (ulong)limit,
            cancellationToken: ct);

        return results.Select(r => new QdrantSearchResult(
            ConversationId: GetPayloadString(r.Payload, "conversation_id") ?? string.Empty,
            Score: r.Score,
            Title: GetPayloadString(r.Payload, "title"),
            Summary: GetPayloadString(r.Payload, "summary")
        )).ToList();
    }

    private static string? GetPayloadString(IReadOnlyDictionary<string, Value> payload, string key)
    {
        return payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;
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

            if (!await _client.CollectionExistsAsync(CollectionName, ct))
            {
                await _client.CreateCollectionAsync(
                    CollectionName,
                    new VectorParams { Size = dimensions, Distance = Distance.Cosine },
                    cancellationToken: ct);

                _logger.LogInformation(
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
