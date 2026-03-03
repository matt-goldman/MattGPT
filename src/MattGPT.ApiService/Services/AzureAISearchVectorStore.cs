using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

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
    public async Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
    {
        await EnsureIndexAsync((int)vector.Length, ct);

        var doc = new SearchDocument
        {
            ["id"] = conversation.ConversationId,
            ["conversation_id"] = conversation.ConversationId,
            ["title"] = conversation.Title ?? string.Empty,
            ["summary"] = conversation.Summary ?? string.Empty,
            ["create_time"] = conversation.CreateTime ?? 0.0,
            ["update_time"] = conversation.UpdateTime ?? 0.0,
            ["default_model_slug"] = conversation.DefaultModelSlug ?? string.Empty,
            ["gizmo_id"] = conversation.GizmoId ?? string.Empty,
            ["is_archived"] = conversation.IsArchived ?? false,
            ["user_id"] = conversation.UserId ?? string.Empty,
            ["vector"] = vector
        };

        await searchClient.MergeOrUploadDocumentsAsync(new[] { doc }, cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Azure AI Search.",
            conversation.ConversationId, conversation.Title);
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
            Select = { "conversation_id", "title", "summary" }
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
                Summary: summary));
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
