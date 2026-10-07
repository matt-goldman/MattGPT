using MattGPT.Contracts.Models;

namespace MattGPT.Contracts.Services;

/// <summary>
/// Provides persistence operations for <see cref="ChatSession"/> documents.
/// </summary>
public interface IChatSessionRepository
{
    /// <summary>Create a new chat session document.</summary>
    Task CreateAsync(ChatSession session, CancellationToken ct = default);

    /// <summary>Retrieve a session by its ID, or null if not found.</summary>
    Task<ChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Append a message to an existing session and update timestamps.</summary>
    Task AddMessageAsync(Guid sessionId, ChatSessionMessage message, CancellationToken ct = default);

    /// <summary>Update the session title.</summary>
    Task UpdateTitleAsync(Guid sessionId, string title, CancellationToken ct = default);

    /// <summary>Update the rolling summary for a session.</summary>
    Task UpdateRollingSummaryAsync(Guid sessionId, string summary, CancellationToken ct = default);

    /// <summary>Update the session status (e.g., Active → Completed).</summary>
    Task UpdateStatusAsync(Guid sessionId, ChatSessionStatus status, CancellationToken ct = default);

    /// <summary>
    /// Mark every active session last updated before <paramref name="idleBefore"/> as
    /// <see cref="ChatSessionStatus.Completed"/>, with <see cref="ChatSessionEmbeddingStatus.Pending"/>
    /// embedding, across all users. Does not change <see cref="ChatSession.UpdatedAt"/>.
    /// </summary>
    /// <returns>The number of sessions completed.</returns>
    Task<long> CompleteIdleSessionsAsync(DateTimeOffset idleBefore, CancellationToken ct = default);

    /// <summary>
    /// Mark the given user's active sessions, other than <paramref name="exceptSessionId"/>, as
    /// completed with pending embedding. Used when the user starts a new chat. Does not change
    /// <see cref="ChatSession.UpdatedAt"/>.
    /// </summary>
    /// <returns>The number of sessions completed.</returns>
    Task<long> CompleteOtherActiveSessionsAsync(string? userId, Guid exceptSessionId, CancellationToken ct = default);

    /// <summary>
    /// Return a completed session to <see cref="ChatSessionStatus.Active"/>, clearing its whole-session
    /// summary and resetting its embedding status to <see cref="ChatSessionEmbeddingStatus.None"/>, because
    /// neither covers the messages about to be added.
    /// </summary>
    Task ReactivateAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Return up to <paramref name="maxCount"/> <see cref="ChatSessionStatus.Completed"/> sessions (with
    /// messages) whose embedding status is one of <paramref name="statuses"/>, across all users, excluding
    /// <paramref name="excludeIds"/>.
    /// </summary>
    Task<List<ChatSession>> GetCompletedByEmbeddingStatusAsync(
        IEnumerable<ChatSessionEmbeddingStatus> statuses, int maxCount,
        IReadOnlyCollection<Guid>? excludeIds = null, CancellationToken ct = default);

    /// <summary>
    /// Record the outcome of storing a completed session in memory: its whole-session summary and
    /// embedding status. Applies only if the session is still completed with exactly
    /// <paramref name="expectedMessageCount"/> messages. This guards against a session that was continued
    /// while it was being processed being marked as embedded with stale content. Does not change
    /// <see cref="ChatSession.UpdatedAt"/>.
    /// </summary>
    /// <returns>Whether the session was updated.</returns>
    Task<bool> UpdateMemoryStateAsync(
        Guid sessionId, string? summary, ChatSessionEmbeddingStatus status, int expectedMessageCount,
        CancellationToken ct = default);

    /// <summary>
    /// Return every embedded session to <see cref="ChatSessionEmbeddingStatus.Pending"/>, so the next
    /// memory sweep projects and embeds it again. Returns the number of sessions reset.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="IConversationRepository.ResetEmbeddingStateAsync"/> for session
    /// projections, which the bulk embed run deliberately leaves alone (ADR-013). A full re-embed has to
    /// cover both, or the corpus is left embedded under two chunking strategies at once. Does not change
    /// <see cref="ChatSession.UpdatedAt"/>, so it does not disturb the idle sweep.
    /// </remarks>
    Task<long> ResetEmbeddingStateAsync(CancellationToken ct = default);

    /// <summary>Return the most recent sessions ordered by <see cref="ChatSession.UpdatedAt"/> descending, scoped to the given user.</summary>
    Task<List<ChatSession>> ListRecentAsync(int limit = 50, string? userId = null, CancellationToken ct = default);
}
