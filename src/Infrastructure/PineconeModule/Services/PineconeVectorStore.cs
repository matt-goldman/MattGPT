using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using Pinecone;

namespace MattGPT.PineconeModule.Services;

/// <summary>
/// Pinecone-backed implementation of <see cref="IVectorStore"/>.
/// Uses the official Pinecone .NET SDK to upsert and query vectors, one vector per conversation chunk.
/// The index must be pre-created in the Pinecone console or via the API.
/// </summary>
public class PineconeVectorStore(
    PineconeClient client,
    ILogger<PineconeVectorStore> logger,
    string indexName = "conversations") : IVectorStore
{
    /// <summary>
    /// How far past the current chunk count ids are deleted when deleting by metadata filter is not
    /// available (Pinecone serverless indexes do not support it). A conversation that previously had
    /// many more chunks than it has now can still leave some behind, which the mixed-strategy
    /// diagnostics would surface; filter deletion, where supported, has no such limit.
    /// </summary>
    private const int PruneOrdinalsAhead = 64;

    /// <inheritdoc/>
    public async Task UpsertAsync(
        StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
    {
        var index = client.Index(indexName);

        // Replace, not merge: a conversation re-embedded into fewer chunks (or under another strategy)
        // must not leave the previous vectors behind to be matched against.
        await DeleteAsync(conversation.ConversationId, chunks.Count, ct);

        if (chunks.Count == 0)
            return;

        var vectors = chunks.Select(c => new Vector
        {
            Id = ChunkPointId.Key(conversation.ConversationId, c.Chunk.Ordinal),
            Values = c.Vector,
            Metadata = new Metadata
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

        await index.UpsertAsync(new UpsertRequest { Vectors = vectors }, cancellationToken: ct);

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Pinecone as {Chunks} chunk(s).",
            conversation.ConversationId, conversation.Title, vectors.Count);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string conversationId, CancellationToken ct = default)
        => DeleteAsync(conversationId, keptChunks: 0, ct);

    /// <summary>
    /// Removes a conversation's vectors. Prefers a metadata-filter delete, which covers every chunk
    /// however many there were; falls back to deleting ids by ordinal when the index does not support
    /// filtered deletes, starting past <paramref name="keptChunks"/> ordinals that are about to be
    /// rewritten anyway.
    /// </summary>
    private async Task DeleteAsync(string conversationId, int keptChunks, CancellationToken ct)
    {
        var index = client.Index(indexName);

        try
        {
            await index.DeleteAsync(
                new DeleteRequest { Filter = new Metadata { ["conversation_id"] = conversationId } },
                cancellationToken: ct);
            return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug(
                ex,
                "Pinecone rejected a filtered delete for conversation {Id} (serverless indexes do not "
                + "support it); deleting chunk ids by ordinal instead.",
                conversationId);
        }

        var ids = Enumerable
            .Range(0, keptChunks + PruneOrdinalsAhead)
            .Select(ordinal => ChunkPointId.Key(conversationId, ordinal))
            .ToList();

        await index.DeleteAsync(new DeleteRequest { Ids = ids }, cancellationToken: ct);
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
            Summary: GetMetadataString(m.Metadata, "summary"),
            Chunk: ReadChunk(m.Metadata)
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

    /// <summary>
    /// The chunk address stored with a vector, or null for a vector written before chunking existed
    /// (which is treated as the whole conversation).
    /// </summary>
    private static ChunkHit? ReadChunk(Metadata? metadata)
    {
        if (GetMetadataInt(metadata, "chunk_ordinal") is not { } ordinal)
            return null;

        var strategy = Enum.TryParse<ChunkingStrategy>(GetMetadataString(metadata, "chunk_strategy"), out var parsed)
            ? parsed
            : ChunkingStrategy.WholeConversation;

        return new ChunkHit(
            Ordinal:           ordinal,
            ChunkCount:        GetMetadataInt(metadata, "chunk_count") ?? 1,
            StartMessageIndex: GetMetadataInt(metadata, "chunk_start_message") ?? 0,
            EndMessageIndex:   GetMetadataInt(metadata, "chunk_end_message") ?? -1,
            Strategy:          strategy);
    }

    private static int? GetMetadataInt(Metadata? metadata, string key)
        => int.TryParse(GetMetadataString(metadata, key), out var value) ? value : null;
}
