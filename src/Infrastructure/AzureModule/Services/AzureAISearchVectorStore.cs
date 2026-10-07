using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;

namespace MattGPT.AzureModule.Services;

/// <summary>
/// Azure AI Search-backed implementation of <see cref="IVectorStore"/>.
/// Creates the index on first use and upserts documents with vector embeddings.
/// Performs vector-based search over stored conversations.
/// </summary>
public class AzureAISearchVectorStore(
    SearchClient searchClient,
    SearchIndexClient indexClient,
    ILogger<AzureAISearchVectorStore> logger) : IVectorStore
{
    private readonly string IndexName = searchClient.IndexName;
    private volatile bool _indexEnsured;
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

        await EnsureIndexAsync(chunks[0].Vector.Length, ct);

        // Replace, not merge: a conversation re-embedded into fewer chunks (or under another strategy)
        // must not leave the previous documents behind to be matched against.
        await DeleteAsync(conversation.ConversationId, ct);

        var docs = chunks.Select(c => new SearchDocument
        {
            ["id"] = ChunkPointId.Key(conversation.ConversationId, c.Chunk.Ordinal),
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
            ["vector"] = c.Vector
        }).ToList();

        await searchClient.MergeOrUploadDocumentsAsync(docs, cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Azure AI Search as {Chunks} chunk(s).",
            conversation.ConversationId, conversation.Title, docs.Count);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        // Azure AI Search deletes by key, so the keys have to be found first: the previous chunk count
        // is not known here, and deriving keys from ordinals would guess at it.
        var escaped = conversationId.Replace("'", "''");
        var options = new SearchOptions
        {
            Filter = $"conversation_id eq '{escaped}'",
            Select = { "id" },
            Size = 1000,
        };

        List<string> keys;
        try
        {
            var response = await searchClient.SearchAsync<SearchDocument>("*", options, ct);
            keys = [];
            await foreach (var result in response.Value.GetResultsAsync())
            {
                if (result.Document.GetString("id") is { Length: > 0 } key)
                    keys.Add(key);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No index yet, so nothing to delete.
            return;
        }

        if (keys.Count == 0)
            return;

        await searchClient.DeleteDocumentsAsync("id", keys, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
    {
        await EnsureIndexAsync((int)queryVector.Length, ct);

        var searchOptions = new SearchOptions
        {
            VectorSearch = new()
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = limit,
                        Fields = { "vector" }
                    }
                }
            },
            Size = limit,
            Select =
            {
                "conversation_id", "title", "summary",
                "chunk_ordinal", "chunk_count", "chunk_start_message", "chunk_end_message", "chunk_strategy",
            }
        };

        if (userId is not null)
        {
            // Escape single quotes to prevent OData filter injection.
            var escaped = userId.Replace("'", "''");
            searchOptions.Filter = $"user_id eq '{escaped}'";
        }

        var response = await searchClient.SearchAsync<SearchDocument>("*", searchOptions, ct);
        var results = new List<VectorSearchResult>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var conversationId = result.Document.GetString("conversation_id");
            var title = result.Document.TryGetValue("title", out var t) ? t?.ToString() : null;
            var summary = result.Document.TryGetValue("summary", out var s) ? s?.ToString() : null;

            results.Add(new VectorSearchResult(
                ConversationId: conversationId ?? string.Empty,
                Score: (float)(result.Score ?? 0.0),
                Title: title,
                Summary: summary,
                Chunk: ReadChunk(result.Document)));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await searchClient.GetDocumentCountAsync(ct);
            return (ulong)response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// The chunk address stored on a document, or null for a document written before chunking existed
    /// (which is treated as the whole conversation).
    /// </summary>
    private static ChunkHit? ReadChunk(SearchDocument document)
    {
        if (ReadInt(document, "chunk_ordinal") is not { } ordinal)
            return null;

        var strategyName = document.TryGetValue("chunk_strategy", out var s) ? s?.ToString() : null;
        var strategy = Enum.TryParse<ChunkingStrategy>(strategyName, out var parsed)
            ? parsed
            : ChunkingStrategy.WholeConversation;

        return new ChunkHit(
            Ordinal:           ordinal,
            ChunkCount:        ReadInt(document, "chunk_count") ?? 1,
            StartMessageIndex: ReadInt(document, "chunk_start_message") ?? 0,
            EndMessageIndex:   ReadInt(document, "chunk_end_message") ?? -1,
            Strategy:          strategy);
    }

    private static int? ReadInt(SearchDocument document, string field)
        => document.TryGetValue(field, out var value) && value is not null
        && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Ensures the Azure AI Search index exists with the correct schema and vector configuration.
    /// Called lazily on the first upsert; subsequent calls are no-ops.
    /// </summary>
    private async Task EnsureIndexAsync(int dimensions, CancellationToken ct)
    {
        if (_indexEnsured) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_indexEnsured) return;

            var index = new SearchIndex(IndexName)
            {
                Fields =
                {
                    new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                    new SearchableField("conversation_id") { IsFilterable = true },
                    new SearchableField("title") { IsFilterable = true },
                    new SearchableField("summary"),
                    new SimpleField("create_time", SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true },
                    new SimpleField("update_time", SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true },
                    new SimpleField("default_model_slug", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("gizmo_id", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("is_archived", SearchFieldDataType.Boolean) { IsFilterable = true },
                    new SimpleField("user_id", SearchFieldDataType.String) { IsFilterable = true },
                    new SimpleField("chunk_ordinal", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
                    new SimpleField("chunk_count", SearchFieldDataType.Int32) { IsFilterable = true },
                    new SimpleField("chunk_start_message", SearchFieldDataType.Int32) { IsFilterable = true },
                    new SimpleField("chunk_end_message", SearchFieldDataType.Int32) { IsFilterable = true },
                    new SimpleField("chunk_strategy", SearchFieldDataType.String) { IsFilterable = true },
                    new SearchField("vector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                    {
                        IsSearchable = true,
                        VectorSearchDimensions = dimensions,
                        VectorSearchProfileName = "vector-profile"
                    }
                },
                VectorSearch = new()
                {
                    Profiles = { new VectorSearchProfile("vector-profile", "hnsw-config") },
                    Algorithms = { new HnswAlgorithmConfiguration("hnsw-config") }
                }
            };

            await indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);

            logger.LogInformation(
                "Ensured Azure AI Search index '{Index}' with {Dims} dimensions.",
                IndexName, dimensions);

            _indexEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
