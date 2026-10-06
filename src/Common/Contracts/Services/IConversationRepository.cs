using MattGPT.Contracts.Models;

namespace MattGPT.Contracts.Services;

/// <summary>
/// Provides persistence operations for <see cref="StoredConversation"/> documents.
/// </summary>
public interface IConversationRepository
{
    /// <summary>Insert or update a conversation document keyed by <see cref="StoredConversation.ConversationId"/>.</summary>
    Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default);

    /// <summary>Return a page of conversations ordered by <see cref="StoredConversation.UpdateTime"/> descending, scoped to the given user.</summary>
    Task<(List<StoredConversation> Items, long Total)> GetPageAsync(int page, int pageSize, string? userId = null, CancellationToken ct = default);

    /// <summary>Return up to <paramref name="maxCount"/> conversations with the given processing status.</summary>
    Task<List<StoredConversation>> GetByStatusAsync(ConversationProcessingStatus status, int maxCount, CancellationToken ct = default);

    /// <summary>
    /// Return up to <paramref name="maxCount"/> conversations matching any of the given processing
    /// statuses, optionally excluding conversations whose id is in <paramref name="excludeIds"/>.
    /// The exclusion lets callers page past conversations they have already handled when processed
    /// items can remain in the matching set (e.g. embedding retries re-mark failures).
    /// </summary>
    Task<List<StoredConversation>> GetByStatusesAsync(IEnumerable<ConversationProcessingStatus> statuses, int maxCount, IReadOnlyCollection<string>? excludeIds = null, CancellationToken ct = default);

    /// <summary>Update the summary text and processing status of a single conversation.</summary>
    Task UpdateSummaryAsync(string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default);

    /// <summary>
    /// Update the processing status of a single conversation. Embedding vectors are not stored on
    /// the conversation document; they live only in the <see cref="IVectorStore"/>.
    /// </summary>
    Task UpdateProcessingStatusAsync(string conversationId, ConversationProcessingStatus status, CancellationToken ct = default);

    /// <summary>Return a single conversation by ID, or null if not found.</summary>
    Task<StoredConversation?> GetByIdAsync(string conversationId, CancellationToken ct = default);

    /// <summary>Return conversations matching the given IDs.</summary>
    Task<List<StoredConversation>> GetByIdsAsync(IEnumerable<string> conversationIds, CancellationToken ct = default);

    /// <summary>
    /// Keyword / full-text search over conversation text (title, summary, and message content),
    /// scoped to the given user and ordered by the backend's relevance rank, best match first.
    /// This is the lexical counterpart to <see cref="IVectorStore.SearchAsync"/>: it matches the
    /// words the user actually typed, so it finds exact identifiers, error codes, and rare names
    /// that semantic search misses - and conversely misses paraphrases that semantic search finds.
    /// </summary>
    /// <remarks>
    /// Each implementation uses its own native text search (MongoDB text indexes, Postgres
    /// tsvector/tsquery), so the query syntax is only guaranteed across backends for the common
    /// subset: word matching is case-insensitive and stemmed, a "quoted phrase" must appear as
    /// written, and a leading <c>-</c> excludes a term. How bare words combine differs and is
    /// deliberately not part of the contract - Postgres requires every term, MongoDB matches any
    /// term and ranks documents containing more of them higher. Rank ordering makes both behave
    /// acceptably for retrieval, but callers must not depend on the exact result set.
    /// Implementations return an empty list rather than throwing when the query has no
    /// searchable terms.
    /// </remarks>
    /// <param name="query">The keyword query. See the remarks for the portable syntax subset.</param>
    /// <param name="maxResults">Maximum number of conversations to return.</param>
    /// <param name="userId">Owner to scope the search to; <c>null</c> matches unowned conversations.</param>
    Task<IReadOnlyList<ConversationTextSearchResult>> SearchTextAsync(
        string query, int maxResults, string? userId = null, CancellationToken ct = default);

    /// <summary>Return the count of conversations grouped by processing status, optionally scoped to a user.</summary>
    Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Return a page of lightweight diagnostic projections (status, summary availability, timestamps)
    /// for the diagnostics view, optionally filtered by processing <paramref name="status"/> and a
    /// case-insensitive title substring <paramref name="titleContains"/>, scoped to the given user and
    /// ordered by <see cref="StoredConversation.UpdateTime"/> descending.
    /// </summary>
    /// <remarks>
    /// Implementations must build rows from a field projection (not a full document deserialize) so
    /// the view stays robust even if a stored document cannot be fully deserialized, and so large
    /// message bodies and embedding vectors are never loaded.
    /// </remarks>
    Task<(IReadOnlyList<ConversationDiagnosticRow> Items, long Total)> GetDiagnosticsPageAsync(
        ConversationProcessingStatus? status, int page, int pageSize,
        string? titleContains = null, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Return project groups (conversations grouped by ConversationTemplateId where GizmoType is "snorlax"), scoped to the given user.
    /// Each group contains the template ID, conversation count, and a representative title.
    /// </summary>
    Task<List<ConversationProject>> GetProjectsAsync(string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Return a page of conversations belonging to a specific project (by ConversationTemplateId), scoped to the given user.
    /// </summary>
    Task<(List<StoredConversation> Items, long Total)> GetProjectConversationsAsync(
        string templateId, int page, int pageSize, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Return a page of conversations that do not belong to any project, scoped to the given user.
    /// </summary>
    Task<(List<StoredConversation> Items, long Total)> GetNonProjectConversationsAsync(
        int page, int pageSize, string? userId = null, CancellationToken ct = default);
}
