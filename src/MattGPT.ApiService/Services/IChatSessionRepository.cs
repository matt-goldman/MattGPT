using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

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

    /// <summary>Return the most recent sessions ordered by <see cref="ChatSession.UpdatedAt"/> descending, scoped to the given user.</summary>
    Task<List<ChatSession>> ListRecentAsync(int limit = 50, string? userId = null, CancellationToken ct = default);
}
