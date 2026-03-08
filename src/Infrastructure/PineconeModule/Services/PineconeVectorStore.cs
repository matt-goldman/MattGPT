using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using Pinecone;

namespace MattGPT.PineconeModule.Services;

/// <summary>
/// Pinecone-backed implementation of <see cref="IVectorStore"/>.
/// Uses the official Pinecone .NET SDK to upsert and query vectors.
/// The index must be pre-created in the Pinecone console or via the API.
/// </summary>
public class PineconeVectorStore(
    PineconeClient client,
    ILogger<PineconeVectorStore> logger,
    string indexName = "conversations") : IVectorStore
{
    /// <inheritdoc/>
    public async Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
    {
        var index = client.Index(indexName);

        var metadata = new Metadata
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
        };

        await index.UpsertAsync(new UpsertRequest
        {
            Vectors = new List<Vector>
            {
                new()
                {
                    Id = conversation.ConversationId,
                    Values = vector,
                    Metadata = metadata
                }
            }
        }, cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Pinecone.",
            conversation.ConversationId, conversation.Title);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
    {
        var index = client.Index(indexName);
        var filterValue = userId ?? string.Empty;

        var response = await index.QueryAsync(new QueryRequest
        {
            Vector = queryVector,
            TopK = (uint)limit,
            IncludeMetadata = true,
            IncludeValues = false,
            Filter = new Metadata { ["user_id"] = filterValue }
        }, cancellationToken: ct);

        if (response.Matches is null || !response.Matches.Any())
            return [];

        return [.. response.Matches.Select(m => new VectorSearchResult(
            ConversationId: GetMetadataString(m.Metadata, "conversation_id") ?? m.Id ?? string.Empty,
            Score: m.Score ?? 0f,
            Title: GetMetadataString(m.Metadata, "title"),
            Summary: GetMetadataString(m.Metadata, "summary")
        ))];
    }

    /// <inheritdoc/>
    public async Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
    {
        try
        {
            var index = client.Index(indexName);
            var stats = await index.DescribeIndexStatsAsync(new DescribeIndexStatsRequest(), cancellationToken: ct);
            return (ulong)(stats.TotalVectorCount ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get Pinecone index stats.");
            return null;
        }
    }

    private static string? GetMetadataString(Metadata? metadata, string key)
    {
        if (metadata is null) return null;
        return metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
