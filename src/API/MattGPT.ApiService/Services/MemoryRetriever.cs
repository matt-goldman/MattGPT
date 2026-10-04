using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Past conversations retrieved for a query: the vector-search hits that met the score
/// threshold (most similar first), and the full stored conversations for those hits keyed by
/// conversation ID. A hit whose conversation could not be loaded has no entry in
/// <see cref="Conversations"/>.
/// </summary>
public record MemoryRetrievalResult(
    IReadOnlyList<VectorSearchResult> Results,
    IReadOnlyDictionary<string, StoredConversation> Conversations)
{
    /// <summary>A result with no hits.</summary>
    public static MemoryRetrievalResult Empty { get; } = new([], new Dictionary<string, StoredConversation>());
}

/// <summary>
/// Semantic retrieval over the user's past conversations: embeds the query, searches the vector
/// store, drops hits below the minimum score, and loads the full conversations for the rest.
/// This is the single retrieval path shared by automatic RAG (<see cref="RagService"/>) and the
/// <c>search_memories</c> tool (<see cref="SearchMemoriesTool"/>), so retrieval changes (e.g.
/// reranking) apply to both.
/// </summary>
public class MemoryRetriever(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IConversationRepository repository,
    ICurrentUserService currentUser,
    ILogger<MemoryRetriever> logger)
{
    /// <summary>
    /// Retrieves up to <paramref name="limit"/> past conversations semantically similar to
    /// <paramref name="query"/> with a similarity score of at least <paramref name="minScore"/>.
    /// </summary>
    /// <param name="query">The text to embed and match by meaning.</param>
    /// <param name="limit">Maximum number of vector-search hits to consider.</param>
    /// <param name="minScore">Minimum similarity score (0.0–1.0) a hit must have to be returned.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<MemoryRetrievalResult> RetrieveAsync(
        string query, int limit, float minScore, CancellationToken ct = default)
    {
        // 1. Embed the query with the same model used to embed the stored conversations.
        var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // 2. Nearest-neighbour search over conversation embeddings, ranked by similarity.
        var searchResults = await vectorStore.SearchAsync(queryVector, limit, currentUser.UserId, ct);

        if (searchResults.Count == 0)
        {
            logger.LogWarning(
                "Vector store returned 0 results for query. Check that embeddings have been generated " +
                "(POST /conversations/embed) and that the vector store collection is populated.");
        }
        else
        {
            foreach (var r in searchResults)
            {
                logger.LogDebug(
                    "Vector result: ConversationId={ConversationId}, Score={Score:F4}, Title={Title}",
                    r.ConversationId, r.Score, r.Title);
            }
        }

        // 3. Apply the minimum similarity threshold.
        var relevant = searchResults
            .Where(r => r.Score >= minScore)
            .ToList();

        logger.LogInformation(
            "Memory retrieval: {Total} results from vector store; {Relevant} meet MinScore threshold of {Threshold:F2}.",
            searchResults.Count, relevant.Count, minScore);

        if (searchResults.Count > 0 && relevant.Count == 0)
        {
            logger.LogWarning(
                "All {Total} vector results were below MinScore={MinScore:F2}. " +
                "Highest score was {MaxScore:F4}. Consider lowering the MinScore setting in configuration.",
                searchResults.Count, minScore, searchResults.Max(r => r.Score));
        }

        if (relevant.Count == 0)
            return MemoryRetrievalResult.Empty;

        // 4. Fetch the full conversations to enrich context.
        var fullConversations = await repository.GetByIdsAsync(relevant.Select(r => r.ConversationId), ct);

        logger.LogInformation(
            "Fetched {Found}/{Requested} full conversations for context enrichment.",
            fullConversations.Count, relevant.Count);

        return new MemoryRetrievalResult(relevant, fullConversations.ToDictionary(c => c.ConversationId));
    }
}
