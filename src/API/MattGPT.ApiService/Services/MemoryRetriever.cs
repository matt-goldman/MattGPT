using MattGPT.ApiService.Extensions;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Past conversations retrieved for a query: the hits (most relevant first), and the full stored
/// conversations for those hits keyed by conversation ID. A hit whose conversation could not be
/// loaded has no entry in <see cref="Conversations"/>.
/// </summary>
public record MemoryRetrievalResult(
    IReadOnlyList<VectorSearchResult> Results,
    IReadOnlyDictionary<string, StoredConversation> Conversations)
{
    /// <summary>A result with no hits.</summary>
    public static MemoryRetrievalResult Empty { get; } = new([], new Dictionary<string, StoredConversation>());
}

/// <summary>
/// Semantic retrieval over the user's past conversations. This is the single retrieval path shared
/// by automatic RAG (<see cref="RagService"/>) and the <c>search_memories</c> tool
/// (<see cref="SearchMemoriesTool"/>); how the search is done is decided here from configuration,
/// so callers only ask for a number of results.
/// </summary>
/// <remarks>
/// <para>
/// Without reranking: embed the query, take the nearest <c>limit</c> conversations from the vector
/// store, drop those below <c>minScore</c>, and load the rest.
/// </para>
/// <para>
/// With reranking (<see cref="RagOptions.UseReranking"/>): take a larger candidate set
/// (<see cref="RagOptions.RerankCandidateCount"/>) with no score threshold, load them, and let the
/// <see cref="IReranker"/> choose and order the top <c>limit</c>. <c>minScore</c> is ignored, and
/// each result's <see cref="VectorSearchResult.Score"/> is the reranker's relevance score. If the
/// reranker fails, the candidates fall back to the non-reranked behaviour so retrieval keeps working.
/// </para>
/// </remarks>
public class MemoryRetriever(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IConversationRepository repository,
    ICurrentUserService currentUser,
    IOptions<RagOptions> options,
    ILogger<MemoryRetriever> logger,
    IReranker? reranker = null)
{
    private readonly RagOptions _options = options.Value;

    /// <summary>Whether retrieval reranks candidates: enabled in config and a reranker is registered.</summary>
    private bool UseReranking => _options.UseReranking && reranker is not null;

    /// <summary>
    /// Retrieves up to <paramref name="limit"/> past conversations relevant to <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The text to search for, matched by meaning.</param>
    /// <param name="limit">Maximum number of conversations to return.</param>
    /// <param name="minScore">
    /// Minimum similarity score (0.0–1.0) a hit must have to be returned. Ignored when reranking.
    /// </param>
    /// <param name="excludeConversationId">
    /// A conversation never to return - the current chat session, so a resumed session does not
    /// retrieve itself.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<MemoryRetrievalResult> RetrieveAsync(
        string query, int limit, float minScore, string? excludeConversationId = null, CancellationToken ct = default)
    {
        var useReranking = UseReranking;
        var candidateCount = useReranking ? Math.Max(limit, _options.RerankCandidateCount) : limit;

        var searchResults = await SearchAsync(query, candidateCount, excludeConversationId, ct);

        if (!useReranking)
            return await FilterAndLoadAsync(searchResults, limit, minScore, ct);

        if (searchResults.Count == 0)
            return MemoryRetrievalResult.Empty;

        // Load every candidate: the reranker reads the conversation text, not the vector.
        var conversations = (await repository.GetByIdsAsync(searchResults.Select(r => r.ConversationId), ct))
            .ToDictionary(c => c.ConversationId);

        IReadOnlyList<RerankResult> reranked;
        try
        {
            var documents = searchResults
                .Select(r => BuildRerankDocument(r, conversations.GetValueOrDefault(r.ConversationId)))
                .ToList();

            reranked = await reranker!.RerankAsync(query, documents, limit, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "Reranking failed; falling back to vector-search order with MinScore={MinScore:F2}.", minScore);
            return await FilterAndLoadAsync(searchResults, limit, minScore, ct, conversations);
        }

        var results = reranked
            .Where(r => r.Index >= 0 && r.Index < searchResults.Count)
            .Select(r => searchResults[r.Index] with { Score = r.Score })
            .ToList();

        logger.LogInformation(
            "Memory retrieval: reranked {Candidates} candidates; returning {Count} (top score {TopScore:F4}).",
            searchResults.Count, results.Count, results.Count > 0 ? results[0].Score : 0f);

        return new MemoryRetrievalResult(results, OnlyFor(results, conversations));
    }

    /// <summary>
    /// Embeds the query and returns the nearest <paramref name="limit"/> conversations, other than
    /// <paramref name="excludeConversationId"/>.
    /// </summary>
    private async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string query, int limit, string? excludeConversationId, CancellationToken ct)
    {
        // Ask for one extra so excluding the current session still leaves a full set.
        var results = await SearchVectorStoreAsync(query, excludeConversationId is null ? limit : limit + 1, ct);
        if (excludeConversationId is null)
            return results;

        return [.. results.Where(r => r.ConversationId != excludeConversationId).Take(limit)];
    }

    private async Task<IReadOnlyList<VectorSearchResult>> SearchVectorStoreAsync(string query, int limit, CancellationToken ct)
    {
        // Embed the query with the same model used to embed the stored conversations.
        var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // Nearest-neighbour search over conversation embeddings, ranked by similarity.
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

        return searchResults;
    }

    /// <summary>
    /// The non-reranked result: the first <paramref name="limit"/> hits at or above
    /// <paramref name="minScore"/>, with their conversations. Uses <paramref name="loaded"/>
    /// when the conversations have already been fetched.
    /// </summary>
    private async Task<MemoryRetrievalResult> FilterAndLoadAsync(
        IReadOnlyList<VectorSearchResult> searchResults,
        int limit,
        float minScore,
        CancellationToken ct,
        IReadOnlyDictionary<string, StoredConversation>? loaded = null)
    {
        var relevant = searchResults
            .Where(r => r.Score >= minScore)
            .Take(limit)
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

        if (loaded is not null)
            return new MemoryRetrievalResult(relevant, OnlyFor(relevant, loaded));

        // Fetch the full conversations to enrich context.
        var fullConversations = await repository.GetByIdsAsync(relevant.Select(r => r.ConversationId), ct);

        logger.LogInformation(
            "Fetched {Found}/{Requested} full conversations for context enrichment.",
            fullConversations.Count, relevant.Count);

        return new MemoryRetrievalResult(relevant, fullConversations.ToDictionary(c => c.ConversationId));
    }

    /// <summary>
    /// The text the reranker scores for one candidate: the same title/summary/messages rendering used
    /// for embedding, capped at <see cref="RagOptions.RerankDocumentChars"/>. Falls back to the title
    /// and summary from the vector store when the conversation could not be loaded.
    /// </summary>
    private string BuildRerankDocument(VectorSearchResult hit, StoredConversation? conversation)
    {
        if (conversation is not null)
            return conversation.ToEmbeddingText(_options.RerankDocumentChars);

        var text = string.Join('\n', new[]
        {
            string.IsNullOrWhiteSpace(hit.Title) ? null : $"Title: {hit.Title}",
            string.IsNullOrWhiteSpace(hit.Summary) ? null : $"Summary: {hit.Summary}",
        }.Where(l => l is not null));

        // Some rerank APIs reject empty documents; keep the candidate's position with a placeholder.
        return text.Length > 0 ? text : "(conversation unavailable)";
    }

    /// <summary>The entries of <paramref name="conversations"/> for <paramref name="hits"/>.</summary>
    private static Dictionary<string, StoredConversation> OnlyFor(
        IEnumerable<VectorSearchResult> hits, IReadOnlyDictionary<string, StoredConversation> conversations)
        => hits
            .Where(h => conversations.ContainsKey(h.ConversationId))
            .DistinctBy(h => h.ConversationId)
            .ToDictionary(h => h.ConversationId, h => conversations[h.ConversationId]);
}
