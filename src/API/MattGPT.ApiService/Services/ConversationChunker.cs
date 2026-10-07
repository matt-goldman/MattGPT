using System.Text;
using MattGPT.ApiService.Extensions;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Divides a conversation into the chunks that are embedded and matched against, under the configured
/// <see cref="ChunkingStrategy"/>, and expands a matching chunk to its neighbours at read time.
/// </summary>
/// <remarks>
/// <para>
/// Chunking is deterministic: the same conversation and strategy always produce the same chunks with
/// the same ordinals. That is what lets a stored chunk hit (which carries only its address) be
/// re-rendered and expanded from the stored conversation, with no chunk text duplicated into the
/// vector store.
/// </para>
/// <para>
/// Strategy is configuration, not an argument, for the same reason reranking is: callers ask for
/// chunks and the deployment decides what a chunk is. The one thing that must not be opaque is which
/// strategy a conversation's vectors were built under, which is recorded on the conversation
/// (<see cref="StoredConversation.EmbeddedChunkingStrategy"/>).
/// </para>
/// </remarks>
public class ConversationChunker(IOptions<RagOptions> options)
{
    private readonly RagOptions _options = options.Value;

    /// <summary>
    /// Share of a chunk's budget the augmentation prefix (title and digest) may take. The prefix pulls
    /// every chunk of a conversation towards a shared centroid, which helps conversation-level recall
    /// and costs intra-chunk discrimination; capping it keeps the balance from inverting on small
    /// chunks, where an untruncated digest would otherwise be most of the vector.
    /// </summary>
    private const double MaxPrefixShare = 0.5;

    /// <summary>The configured strategy, recorded with every conversation this chunker embeds.</summary>
    public ChunkingStrategy Strategy => _options.ChunkingStrategy;

    /// <summary>
    /// Divides <paramref name="conversation"/> into chunks under the configured strategy. Returns an
    /// empty list when the conversation has no embeddable content.
    /// </summary>
    public IReadOnlyList<ConversationChunk> Chunk(StoredConversation conversation)
        => Chunk(conversation, Strategy, _options.MaxChunkChars, _options.EffectiveMaxEmbeddingChars);

    /// <summary>
    /// Divides <paramref name="conversation"/> into chunks under an explicit strategy.
    /// </summary>
    /// <param name="conversation">The conversation to chunk.</param>
    /// <param name="strategy">The strategy to apply.</param>
    /// <param name="maxChunkChars">Maximum characters per chunk for the per-unit strategies.</param>
    /// <param name="maxWholeConversationChars">
    /// Maximum characters for <see cref="ChunkingStrategy.WholeConversation"/>, which is bounded by the
    /// embedding model's context rather than by chunk size.
    /// </param>
    public static IReadOnlyList<ConversationChunk> Chunk(
        StoredConversation conversation,
        ChunkingStrategy strategy,
        int maxChunkChars,
        int maxWholeConversationChars)
    {
        if (strategy == ChunkingStrategy.WholeConversation)
        {
            var text = conversation.ToEmbeddingText(maxWholeConversationChars);
            if (string.IsNullOrWhiteSpace(text))
                return [];

            return
            [
                new ConversationChunk(
                    ConversationId:    conversation.ConversationId,
                    Ordinal:           0,
                    StartMessageIndex: 0,
                    EndMessageIndex:   Math.Max(0, conversation.LinearisedMessages.Count - 1),
                    EmbeddingText:     text,
                    Strategy:          ChunkingStrategy.WholeConversation,
                    Timestamp:         conversation.CreateTime),
            ];
        }

        var prefix = BuildPrefix(conversation, maxChunkChars);
        var chunks = new List<ConversationChunk>();

        foreach (var unit in Units(conversation, strategy))
            AddUnit(chunks, conversation, unit, strategy, prefix, maxChunkChars);

        return chunks;
    }

    /// <summary>
    /// The chunks of <paramref name="conversation"/> whose content is carried to generation for a set of
    /// matching chunk ordinals: each match widened by <see cref="RagOptions.NeighbourWindow"/> chunks
    /// either side, with overlapping windows merged so no message is included twice.
    /// </summary>
    /// <remarks>
    /// Read-time expansion is what makes storing chunks without overlap defensible: a thought that spans
    /// several turns is split across chunks, and only one of them needs to match for the whole thought to
    /// reach the model (ADR-016). Ordinals that no longer exist — because the conversation changed since
    /// it was embedded — are ignored rather than clamped onto the wrong chunk.
    /// </remarks>
    /// <param name="conversation">The conversation the ordinals refer to.</param>
    /// <param name="matchedOrdinals">Ordinals of the chunks that matched the query.</param>
    /// <param name="window">Neighbours either side; defaults to the configured window.</param>
    public IReadOnlyList<ConversationChunk> Expand(
        StoredConversation conversation, IEnumerable<int> matchedOrdinals, int? window = null)
    {
        var all = Chunk(conversation);
        return Expand(all, matchedOrdinals, window ?? _options.NeighbourWindow);
    }

    /// <summary>
    /// Expands matching ordinals over an already-computed chunk list. See
    /// <see cref="Expand(StoredConversation, IEnumerable{int}, int?)"/>.
    /// </summary>
    public static IReadOnlyList<ConversationChunk> Expand(
        IReadOnlyList<ConversationChunk> chunks, IEnumerable<int> matchedOrdinals, int window)
    {
        if (chunks.Count == 0)
            return [];

        var keep = new SortedSet<int>();

        foreach (var ordinal in matchedOrdinals)
        {
            // An ordinal from a stale embedding can point past the end; it identifies nothing, so
            // including a neighbour of it would be including an unrelated chunk.
            if (ordinal < 0 || ordinal >= chunks.Count)
                continue;

            var from = Math.Max(0, ordinal - Math.Max(0, window));
            var to = Math.Min(chunks.Count - 1, ordinal + Math.Max(0, window));

            for (var i = from; i <= to; i++)
                keep.Add(i);
        }

        // A SortedSet of ordinals is the deduplication: two matches whose windows overlap contribute
        // the shared chunks once, and the result is in document order.
        return [.. keep.Select(i => chunks[i])];
    }

    /// <summary>
    /// The units a strategy divides a conversation into, before any oversized unit is split: one
    /// embeddable message each for <see cref="ChunkingStrategy.Message"/>, or a user turn plus the
    /// turns that follow it for <see cref="ChunkingStrategy.Exchange"/>.
    /// </summary>
    private static IEnumerable<(int Start, int End)> Units(StoredConversation conversation, ChunkingStrategy strategy)
    {
        var messages = conversation.LinearisedMessages;

        if (strategy == ChunkingStrategy.Message)
        {
            for (var i = 0; i < messages.Count; i++)
            {
                if (messages[i].IsEmbeddable())
                    yield return (i, i);
            }

            yield break;
        }

        // Exchange: a user turn plus everything up to the next user turn. Messages before the first
        // user turn form a unit of their own rather than being attached to it, so a chunk never spans
        // two user turns.
        var start = -1;
        var end = -1;

        for (var i = 0; i < messages.Count; i++)
        {
            if (!messages[i].IsEmbeddable())
                continue;

            var isUser = string.Equals(messages[i].Role, "user", StringComparison.OrdinalIgnoreCase);

            if (isUser && start >= 0)
            {
                yield return (start, end);
                start = -1;
            }

            if (start < 0)
                start = i;

            end = i;
        }

        if (start >= 0)
            yield return (start, end);
    }

    /// <summary>
    /// Renders one unit and appends it to <paramref name="chunks"/> as a single chunk, or — when it is
    /// longer than the budget — as several chunks that carry the ordinal of the unit's first part as
    /// their parent, so a split is visible in the address rather than silent.
    /// </summary>
    private static void AddUnit(
        List<ConversationChunk> chunks,
        StoredConversation conversation,
        (int Start, int End) unit,
        ChunkingStrategy strategy,
        string prefix,
        int maxChunkChars)
    {
        var budget = Math.Max(1, maxChunkChars - prefix.Length);

        // Render generously, then split: the renderer truncates at its limit, so asking for only the
        // budget would silently drop the tail of a long unit instead of splitting it.
        var body = conversation.ToRangeEmbeddingText(unit.Start, unit.End, budget * 8);
        if (string.IsNullOrWhiteSpace(body))
            return;

        List<string> parts = body.Length <= budget ? [body] : SplitOnLines(body, budget);
        var headOrdinal = chunks.Count;

        for (var part = 0; part < parts.Count; part++)
        {
            chunks.Add(new ConversationChunk(
                ConversationId:    conversation.ConversationId,
                Ordinal:           chunks.Count,
                StartMessageIndex: unit.Start,
                EndMessageIndex:   unit.End,
                EmbeddingText:     prefix + parts[part],
                Strategy:          strategy,
                ParentOrdinal:     part == 0 ? null : headOrdinal,
                Actor:             Actor(conversation, unit),
                Timestamp:         conversation.LinearisedMessages[unit.Start].CreateTime));
        }
    }

    /// <summary>
    /// The role that owns a unit: the role of every message in it, or null when it mixes roles (an
    /// exchange normally does).
    /// </summary>
    private static string? Actor(StoredConversation conversation, (int Start, int End) unit)
    {
        var role = conversation.LinearisedMessages[unit.Start].Role;

        for (var i = unit.Start + 1; i <= unit.End; i++)
        {
            if (conversation.LinearisedMessages[i].IsEmbeddable()
                && !string.Equals(conversation.LinearisedMessages[i].Role, role, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return role;
    }

    /// <summary>
    /// The augmentation prefix every chunk of a conversation carries: its title and digest, so a chunk
    /// is situated in the conversation it came from rather than embedded as a bare fragment. Capped at
    /// <see cref="MaxPrefixShare"/> of the chunk budget.
    /// </summary>
    private static string BuildPrefix(StoredConversation conversation, int maxChunkChars)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(conversation.Title))
            sb.Append("Title: ").AppendLine(conversation.Title);

        if (!string.IsNullOrWhiteSpace(conversation.Summary))
            sb.Append("Summary: ").AppendLine(conversation.Summary);

        var cap = (int)(maxChunkChars * MaxPrefixShare);
        return sb.Length <= cap ? sb.ToString() : sb.ToString(0, cap).TrimEnd() + "\n";
    }

    /// <summary>
    /// Splits <paramref name="text"/> into pieces of at most <paramref name="maxChars"/>, breaking on
    /// the last line boundary in each window so a split lands between messages where it can.
    /// </summary>
    private static List<string> SplitOnLines(string text, int maxChars)
    {
        var parts = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + maxChars, text.Length);

            if (end < text.Length)
            {
                var newline = text.LastIndexOf('\n', end - 1, end - start);
                if (newline > start)
                    end = newline + 1;
            }

            parts.Add(text[start..end]);
            start = end;
        }

        return parts;
    }
}
