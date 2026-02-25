using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides persistence operations for <see cref="StoredConversation"/> documents.
/// </summary>
public interface IConversationRepository
{
    /// <summary>Insert or update a conversation document keyed by <see cref="StoredConversation.ConversationId"/>.</summary>
    Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default);

    /// <summary>Return a page of conversations ordered by <see cref="StoredConversation.ImportTimestamp"/> descending.</summary>
    Task<(List<StoredConversation> Items, long Total)> GetPageAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Return up to <paramref name="maxCount"/> conversations with the given processing status.</summary>
    Task<List<StoredConversation>> GetByStatusAsync(ConversationProcessingStatus status, int maxCount, CancellationToken ct = default);

    /// <summary>Update the summary text and processing status of a single conversation.</summary>
    Task UpdateSummaryAsync(string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default);
}
