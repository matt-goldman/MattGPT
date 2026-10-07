using MattGPT.ApiService.Extensions;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Past conversations retrieved for a query: the retained conversations (most relevant first), the full
/// stored conversations for them keyed by conversation ID, and the chunks of each that matched. A hit
/// whose conversation could not be loaded has no entry in <see cref="Conversations"/> or
/// <see cref="MatchingChunks"/>.
/// </summary>
/// <param name="Results">
/// One entry per retained conversation, not per chunk: <see cref="VectorSearchResult.Score"/> is the
/// conversation's pooled (or reranked) score, and <see cref="VectorSearchResult.Chunk"/> is its
/// best-matching chunk.
/// </param>
/// <param name="Conversations">The retained conversations, keyed by id.</param>
/// <param name="MatchingChunks">
/// Per conversation, the chunks carried to generation: the matching chunks plus their neighbours, in
/// document order, with overlapping windows merged. Empty for a conversation that could not be loaded.
/// </param>
public record MemoryRetrievalResult(
    IReadOnlyList<VectorSearchResult> Results,
    IReadOnlyDictionary<string, StoredConversation> Conversations,
    IReadOnlyDictionary<string, IReadOnlyList<ConversationChunk>> MatchingChunks)
{
    /// <summary>A result with no hits.</summary>
    public static MemoryRetrievalResult Empty { get; } = new(
        [],
        new Dictionary<string, StoredConversation>(),
        new Dictionary<string, IReadOnlyList<ConversationChunk>>());
}

/// <summary>
/// Semantic retrieval over the user's past conversations. This is the single retrieval path shared
/// by automatic RAG (<see cref="RagService"/>) and the <c>search_memories</c> tool
/// (<see cref="SearchMemoriesTool"/>); how the search is done is decided here from configuration,
/// so callers only ask for a number of results.
/// </summary>
/// <remarks>
/// <para>
/// The conversation is the unit of retrieval, ranking and citation; the chunk is only evidence of a
/// conversation's relevance (ADR-016). So every path here ends in a set of conversations:
/// </para>
/// <list type="number">
/// <item>embed the query and take the nearest <em>chunks</em> — several per conversation wanted, since
/// chunks of one conversation compete for the same top-k;</item>
/// <item>pool chunk scores to their conversation by <em>maximum</em> (not sum, which would reward
/// length);</item>
/// <item>order the conversations — by pooled score, or by the <see cref="IReranker"/> scoring one
/// document per conversation when <see cref="RagOptions.UseReranking"/> is on;</item>
/// <item>retain several of them (<see cref="RagOptions.RetainedConversations"/> plus
/// <see cref="RagOptions.RetainedRelativeScore"/>) rather than cutting to the best one;</item>
/// <item>expand each retained conversation's matching chunks to their neighbours, so a thought that
/// spans turns survives chunking without storing overlapping vectors.</item>
/// </list>
/// <para>
/// Without reranking, <c>minScore</c> still applies first. With reranking it is ignored, and each
/// result's <see cref="VectorSearchResult.Score"/> is the reranker's relevance score. If the reranker
/// fails, the candidates fall back to the non-reranked behaviour so retrieval keeps working.
/// </para>
/// </remarks>
public class MemoryRetriever(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IConversationRepository repository,
    ICurrentUserService currentUser,
    ConversationChunker chunker,
    IOptions<RagOptions> options,
    ILogger<MemoryRetriever> logger,
    IReranker? reranker = null)
{
    private readonly RagOptions _options = options.Value;

    /// <summary>Whether retrieval reranks candidates: enabled in config and a reranker is registered.</summary>
    private bool UseReranking => _options.UseReranking && reranker is not null;

    /// <summary>
    /// A conversation assembled from the chunk hits that pooled to it: its best chunk hit (the pooled
    /// score and address) and the ordinals of every chunk of it that matched.
    /// </summary>
    private sealed record Candidate(VectorSearchResult Best, List<int> MatchedOrdinals)
    {
        public string ConversationId => Best.ConversationId;
    }

    /// <summary>
    /// Retrieves past conversations relevant to <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The text to search for, matched by meaning.</param>
    /// <param name="limit">
    /// Minimum number of conversations the caller wants. The number actually retained is
    /// <c>max(limit, <see cref="RagOptions.RetainedConversations"/>)</c>, filtered by
    /// <see cref="RagOptions.RetainedRelativeScore"/>: breadth is a configuration decision, so a small
    /// top-k cannot narrow the context below it.
    /// </param>
    /// <param name="minScore">
    /// Minimum similarity score (0.0–1.0) a conversation's pooled score must have to be returned.
    /// Ignored when reranking.
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

        var conversationCandidates = Math.Max(
            useReranking ? Math.Max(limit, _options.RerankCandidateCount) : limit,
            _options.RetainedConversations);

        // Ask for several chunks per conversation wanted: with one vector per chunk, a single
        // many-chunk conversation would otherwise fill the candidate set on its own.
        var chunkCandidates = conversationCandidates * Math.Max(1, _options.ChunkCandidateMultiplier);

        var chunkHits = await SearchAsync(query, chunkCandidates, excludeConversationId, ct);
        var pooled = PoolByConversation(chunkHits);

        if (pooled.Count == 0)
            return MemoryRetrievalResult.Empty;

        if (!useReranking)
            return await FilterAndLoadAsync(pooled, limit, minScore, ct);

        // Load every candidate: the reranker reads the conversation's text, not the vector.
        var conversations = (await repository.GetByIdsAsync(pooled.Select(c => c.ConversationId), ct))
            .ToDictionary(c => c.ConversationId);

        IReadOnlyList<RerankResult> reranked;
        try
        {
            var documents = pooled
                .Select(c => BuildRerankDocument(c, conversations.GetValueOrDefault(c.ConversationId)))
                .ToList();

            reranked = await reranker!.RerankAsync(query, documents, Math.Max(limit, _options.RetainedConversations), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "Reranking failed; falling back to vector-search order with MinScore={MinScore:F2}.", minScore);
            return await FilterAndLoadAsync(pooled, limit, minScore, ct, conversations);
        }

        var ranked = reranked
            .Where(r => r.Index >= 0 && r.Index < pooled.Count)
            .Select(r => pooled[r.Index] with { Best = pooled[r.Index].Best with { Score = r.Score } })
            .ToList();

        var retained = ApplyRetainedBreadth(ranked, limit);

        logger.LogInformation(
            "Memory retrieval: {ChunkHits} chunk hit(s) over {Candidates} conversation(s); reranked and retained "
            + "{Retained} (top score {TopScore:F4}).",
            chunkHits.Count, pooled.Count, retained.Count, retained.Count > 0 ? retained[0].Best.Score : 0f);

        return Build(retained, conversations);
    }

    /// <summary>
    /// Embeds the query and returns the nearest <paramref name="limit"/> chunks, other than those of
    /// <paramref name="excludeConversationId"/>.
    /// </summary>
    private async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string query, int limit, string? excludeConversationId, CancellationToken ct)
    {
        var results = await SearchVectorStoreAsync(query, limit, ct);
        if (excludeConversationId is null)
            return results;

        return [.. results.Where(r => r.ConversationId != excludeConversationId)];
    }

    private async Task<IReadOnlyList<VectorSearchResult>> SearchVectorStoreAsync(string query, int limit, CancellationToken ct)
    {
        // Embed the query with the same model used to embed the stored conversations.
        var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // Nearest-neighbour search over conversation chunk embeddings, ranked by similarity.
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
                    "Vector result: ConversationId={ConversationId}, Chunk={Chunk}, Score={Score:F4}, Title={Title}",
                    r.ConversationId, r.Chunk?.Ordinal ?? 0, r.Score, r.Title);
            }
        }

        return searchResults;
    }

    /// <summary>
    /// Pools chunk hits to their conversations by <em>maximum</em> score, best conversation first.
    /// </summary>
    /// <remarks>
    /// Max, not sum: summing rewards length, and this corpus's longest conversations are substantive
    /// working sessions that would then dominate unrelated queries. The cost is that corroboration is
    /// discarded — a conversation matching weakly in eight places scores the same as one matching weakly
    /// once — so the number of matching chunks is kept (as <see cref="Candidate.MatchedOrdinals"/>) and
    /// logged, which is what a count-aware tiebreak would need if evaluation shows it matters.
    /// </remarks>
    private static List<Candidate> PoolByConversation(IReadOnlyList<VectorSearchResult> chunkHits)
    {
        var byConversation = new Dictionary<string, Candidate>();
        var order = new List<string>();

        foreach (var hit in chunkHits)
        {
            if (byConversation.TryGetValue(hit.ConversationId, out var candidate))
            {
                candidate.MatchedOrdinals.Add(hit.Chunk?.Ordinal ?? 0);

                if (hit.Score > candidate.Best.Score)
                    byConversation[hit.ConversationId] = candidate with { Best = hit };
            }
            else
            {
                byConversation[hit.ConversationId] = new Candidate(hit, [hit.Chunk?.Ordinal ?? 0]);
                order.Add(hit.ConversationId);
            }
        }

        // OrderByDescending is stable, so conversations with equal pooled scores keep the order the
        // vector store returned them in.
        return [.. order.Select(id => byConversation[id]).OrderByDescending(c => c.Best.Score)];
    }

    /// <summary>
    /// The conversations retained from a ranked list: at most
    /// <c>max(limit, <see cref="RagOptions.RetainedConversations"/>)</c>, and only those scoring at least
    /// <see cref="RagOptions.RetainedRelativeScore"/> of the best score.
    /// </summary>
    /// <remarks>
    /// Breadth is explicit here rather than a side effect of top-k, because cutting to one conversation
    /// is a regression for any question whose answer spans conversations. The relative threshold is the
    /// other half of that bargain: it keeps a query with one clearly dominant conversation from padding
    /// the context with weak matches. It is relative because scores are not comparable across stores or
    /// between the cosine and reranker scales, and it is skipped when the best score is not positive,
    /// where a ratio means nothing.
    /// </remarks>
    private List<Candidate> ApplyRetainedBreadth(IReadOnlyList<Candidate> ranked, int limit)
    {
        if (ranked.Count == 0)
            return [];

        var cap = Math.Max(limit, _options.RetainedConversations);

        // The best score, not the first one: the order is the ranker's business, and a reranker that
        // returns its results unsorted must not make the floor meaningless.
        var topScore = ranked.Max(c => c.Best.Score);
        var applyThreshold = _options.RetainedRelativeScore > 0f && topScore > 0f;
        var floor = topScore * _options.RetainedRelativeScore;

        var retained = new List<Candidate>(Math.Min(cap, ranked.Count));

        foreach (var candidate in ranked)
        {
            if (applyThreshold && candidate.Best.Score < floor)
                continue;

            retained.Add(candidate);

            if (retained.Count >= cap)
                break;
        }

        if (retained.Count < ranked.Count)
        {
            logger.LogDebug(
                "Retained {Retained}/{Ranked} conversation(s): cap {Cap}, relative floor {Floor:F4} of top {Top:F4}.",
                retained.Count, ranked.Count, cap, applyThreshold ? floor : 0f, topScore);
        }

        return retained;
    }

    /// <summary>
    /// The non-reranked result: the conversations whose pooled score is at or above
    /// <paramref name="minScore"/>, cut to the retained breadth, with their conversations and matching
    /// chunks. Uses <paramref name="loaded"/> when the conversations have already been fetched.
    /// </summary>
    private async Task<MemoryRetrievalResult> FilterAndLoadAsync(
        IReadOnlyList<Candidate> pooled,
        int limit,
        float minScore,
        CancellationToken ct,
        IReadOnlyDictionary<string, StoredConversation>? loaded = null)
    {
        var relevant = pooled.Where(c => c.Best.Score >= minScore).ToList();

        logger.LogInformation(
            "Memory retrieval: {Total} conversation(s) from vector search; {Relevant} meet MinScore threshold of {Threshold:F2}.",
            pooled.Count, relevant.Count, minScore);

        if (pooled.Count > 0 && relevant.Count == 0)
        {
            logger.LogWarning(
                "All {Total} vector results were below MinScore={MinScore:F2}. " +
                "Highest score was {MaxScore:F4}. Consider lowering the MinScore setting in configuration.",
                pooled.Count, minScore, pooled.Max(c => c.Best.Score));
        }

        if (relevant.Count == 0)
            return MemoryRetrievalResult.Empty;

        var retained = ApplyRetainedBreadth(relevant, limit);

        if (loaded is not null)
            return Build(retained, loaded);

        // Fetch the full conversations to enrich context.
        var fullConversations = await repository.GetByIdsAsync(retained.Select(c => c.ConversationId), ct);

        logger.LogInformation(
            "Fetched {Found}/{Requested} full conversations for context enrichment.",
            fullConversations.Count, retained.Count);

        return Build(retained, fullConversations.ToDictionary(c => c.ConversationId));
    }

    /// <summary>
    /// Assembles the result for a retained set: one hit per conversation, the conversations that could be
    /// loaded, and each one's matching chunks expanded to their neighbours.
    /// </summary>
    private MemoryRetrievalResult Build(
        IReadOnlyList<Candidate> retained, IReadOnlyDictionary<string, StoredConversation> conversations)
    {
        var results = retained.Select(c => c.Best).ToList();
        var loaded = OnlyFor(results, conversations);
        var chunks = new Dictionary<string, IReadOnlyList<ConversationChunk>>();

        foreach (var candidate in retained)
        {
            if (!loaded.TryGetValue(candidate.ConversationId, out var conversation))
                continue;

            var expanded = chunker.Expand(conversation, candidate.MatchedOrdinals);
            if (expanded.Count > 0)
                chunks[candidate.ConversationId] = expanded;
        }

        return new MemoryRetrievalResult(results, loaded, chunks);
    }

    /// <summary>
    /// The text the reranker scores for one candidate conversation. The reranker orders conversations,
    /// never chunks, so there is exactly one document per conversation.
    /// </summary>
    /// <remarks>
    /// The document is, in order of availability, the conversation's record, its digest, or a stitched
    /// window of its matching chunks (ADR-016) — the record is the resolved account of what the
    /// conversation concluded, the digest is the next best thing, and the matching chunks are what the
    /// query actually hit. Capped at <see cref="RagOptions.RerankDocumentChars"/>, because reranking
    /// models have small context windows and every candidate travels in one request. Falls back to the
    /// title and summary from the vector store when the conversation could not be loaded.
    /// </remarks>
    private string BuildRerankDocument(Candidate candidate, StoredConversation? conversation)
    {
        var cap = _options.RerankDocumentChars;
        var hit = candidate.Best;

        if (conversation is null)
        {
            var fromHit = string.Join('\n', new[]
            {
                string.IsNullOrWhiteSpace(hit.Title) ? null : $"Title: {hit.Title}",
                string.IsNullOrWhiteSpace(hit.Summary) ? null : $"Summary: {hit.Summary}",
            }.Where(l => l is not null));

            // Some rerank APIs reject empty documents; keep the candidate's position with a placeholder.
            return Truncate(fromHit.Length > 0 ? fromHit : "(conversation unavailable)", cap);
        }

        var header = string.IsNullOrWhiteSpace(conversation.Title) ? string.Empty : $"Title: {conversation.Title}\n";
        var budget = Math.Max(1, cap - header.Length);

        if (!string.IsNullOrWhiteSpace(conversation.Record))
            return Truncate(header + "Record: " + conversation.Record, cap);

        if (!string.IsNullOrWhiteSpace(conversation.Summary))
            return Truncate(header + "Summary: " + conversation.Summary, cap);

        // No resolved account of this conversation exists, so stitch the evidence that matched. Matched
        // chunks only, not their neighbours: this document decides relevance, and the neighbours are for
        // the model that reads the retained conversation afterwards.
        var matched = ConversationChunker.Expand(chunker.Chunk(conversation), candidate.MatchedOrdinals, window: 0);

        if (matched.Count == 0)
            return Truncate(header.Length > 0 ? header : "(conversation unavailable)", cap);

        if (matched is [{ Strategy: ChunkingStrategy.WholeConversation }])
            return conversation.ToEmbeddingText(cap);

        var stitched = string.Join(
            "\n…\n",
            matched.Select(c => conversation.ToRangeExcerpt(c.StartMessageIndex, c.EndMessageIndex, budget)));

        return Truncate(header + stitched, cap);
    }

    private static string Truncate(string text, int maxChars)
        => text.Length <= maxChars ? text : text[..maxChars];

    /// <summary>The entries of <paramref name="conversations"/> for <paramref name="hits"/>.</summary>
    private static Dictionary<string, StoredConversation> OnlyFor(
        IEnumerable<VectorSearchResult> hits, IReadOnlyDictionary<string, StoredConversation> conversations)
        => hits
            .Where(h => conversations.ContainsKey(h.ConversationId))
            .DistinctBy(h => h.ConversationId)
            .ToDictionary(h => h.ConversationId, h => conversations[h.ConversationId]);
}
