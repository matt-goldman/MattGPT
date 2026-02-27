using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides persistence operations for <see cref="StoredConversation"/> documents.
/// </summary>
public interface IConversationRepository
{
    /// <summary>Insert or update a conversation document keyed by <see cref="StoredConversation.ConversationId"/>.</summary>
    Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default);

    /// <summary>Return a page of conversations ordered by <see cref="StoredConversation.UpdateTime"/> descending.</summary>
    Task<(List<StoredConversation> Items, long Total)> GetPageAsync(int page, int pageSize, CancellationToken ct = default);

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

    /// <summary>Return the count of conversations grouped by processing status.</summary>
    Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(CancellationToken ct = default);
}
