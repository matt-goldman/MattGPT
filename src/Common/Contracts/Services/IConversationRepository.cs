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

    /// <summary>Return up to <paramref name="maxCount"/> conversations matching any of the given processing statuses.</summary>
    Task<List<StoredConversation>> GetByStatusesAsync(IEnumerable<ConversationProcessingStatus> statuses, int maxCount, CancellationToken ct = default);

    /// <summary>Update the summary text and processing status of a single conversation.</summary>
    Task UpdateSummaryAsync(string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default);

    /// <summary>Update the embedding vector and processing status of a single conversation.</summary>
    Task UpdateEmbeddingAsync(string conversationId, float[]? embedding, ConversationProcessingStatus status, CancellationToken ct = default);

    /// <summary>Return a single conversation by ID, or null if not found.</summary>
    Task<StoredConversation?> GetByIdAsync(string conversationId, CancellationToken ct = default);

    /// <summary>Return conversations matching the given IDs.</summary>
    Task<List<StoredConversation>> GetByIdsAsync(IEnumerable<string> conversationIds, CancellationToken ct = default);

    /// <summary>Return the count of conversations grouped by processing status, optionally scoped to a user.</summary>
    Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(string? userId = null, CancellationToken ct = default);

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
