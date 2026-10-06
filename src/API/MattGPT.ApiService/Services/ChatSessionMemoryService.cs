using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>Result of a chat session memory sweep.</summary>
/// <param name="Completed">Idle sessions marked completed by this sweep.</param>
/// <param name="Embedded">Completed sessions summarised, projected and embedded.</param>
/// <param name="Errors">Completed sessions that could not be stored; left in <see cref="ChatSessionEmbeddingStatus.Error"/>.</param>
public record ChatSessionMemoryResult(long Completed, int Embedded, int Errors);

/// <summary>
/// Turns completed chat sessions into memory (ADR-013): generates a whole-session summary, projects
/// the session into a <see cref="StoredConversation"/> in the conversation repository, and embeds it
/// through <see cref="EmbeddingService"/>, so it is retrieved exactly like an imported conversation.
/// </summary>
/// <remarks>
/// Completion itself (a status flip) happens elsewhere: on a new chat in <see cref="ChatSessionService"/>,
/// or here for idle sessions. This service does the slow part, from the background
/// (<see cref="ChatSessionLifecycleService"/>) or the embed job's backfill.
/// </remarks>
public class ChatSessionMemoryService(
    IChatSessionRepository sessions,
    IConversationRepository conversations,
    SummarisationService summariser,
    EmbeddingService embedder,
    IOptions<ChatSessionOptions> options,
    TimeProvider timeProvider,
    ILogger<ChatSessionMemoryService> logger)
{
    /// <summary>Number of sessions loaded per batch.</summary>
    private const int BatchSize = 20;

    /// <summary>
    /// Completes sessions idle past <see cref="ChatSessionOptions.IdleTimeout"/>, then stores every
    /// pending session in memory. With <paramref name="retryErrors"/>, sessions that previously failed
    /// are retried too. This is the backfill: sessions that predate session embedding are active with
    /// old timestamps, so they are completed and stored here.
    /// </summary>
    public async Task<ChatSessionMemoryResult> SweepAsync(bool retryErrors, CancellationToken ct = default)
    {
        var idleBefore = timeProvider.GetUtcNow() - options.Value.IdleTimeout;
        var completed = await sessions.CompleteIdleSessionsAsync(idleBefore, ct);
        if (completed > 0)
            logger.LogInformation("Completed {Count} chat session(s) idle since before {IdleBefore:u}.", completed, idleBefore);

        ChatSessionEmbeddingStatus[] statuses = retryErrors
            ? [ChatSessionEmbeddingStatus.Pending, ChatSessionEmbeddingStatus.Error]
            : [ChatSessionEmbeddingStatus.Pending];

        var embedded = 0;
        var errors = 0;

        // A failed session stays in Error, which keeps it in the retry set; exclude sessions already
        // attempted so each is tried at most once per sweep (same pattern as EmbeddingService).
        var attempted = new HashSet<Guid>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await sessions.GetCompletedByEmbeddingStatusAsync(statuses, BatchSize, attempted, ct);
            if (batch.Count == 0)
                break;

            foreach (var session in batch)
            {
                if (ct.IsCancellationRequested)
                    break;

                attempted.Add(session.SessionId);

                if (await StoreAsync(session, ct))
                    embedded++;
                else
                    errors++;
            }
        }

        if (embedded > 0 || errors > 0)
        {
            logger.LogInformation(
                "Chat session memory sweep: {Embedded} session(s) embedded, {Errors} error(s).", embedded, errors);
        }

        return new ChatSessionMemoryResult(completed, embedded, errors);
    }

    /// <summary>
    /// Summarises, projects and embeds one completed session, and records the outcome on it.
    /// </summary>
    /// <returns>Whether the session is now embedded.</returns>
    public async Task<bool> StoreAsync(ChatSession session, CancellationToken ct = default)
    {
        if (session.Messages.Count == 0)
        {
            // Nothing to remember. Clear Pending so it isn't picked up again.
            await sessions.UpdateMemoryStateAsync(session.SessionId, null, ChatSessionEmbeddingStatus.None, 0, ct);
            return true;
        }

        // Reactivation clears the summary, so one that is present still covers the whole session
        // (e.g. this is a retry after an embedding failure) and need not be regenerated.
        var summary = session.Summary ?? await TrySummariseAsync(session, ct);

        var embedded = false;
        try
        {
            var projection = StoredConversation.FromChatSession(session, summary);
            await conversations.UpsertAsync(projection, ct);
            embedded = await embedder.EmbedOneAsync(projection, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to store chat session {SessionId} in memory.", session.SessionId);
        }

        var status = embedded ? ChatSessionEmbeddingStatus.Embedded : ChatSessionEmbeddingStatus.Error;
        var applied = await sessions.UpdateMemoryStateAsync(session.SessionId, summary, status, session.Messages.Count, ct);

        if (!applied)
        {
            // The session was continued while being processed. What was embedded is still a valid
            // (older) memory of it, and it will be stored again when it next completes.
            logger.LogInformation(
                "Chat session {SessionId} changed while being stored; it will be re-embedded when it next completes.",
                session.SessionId);
        }
        else
        {
            logger.LogDebug("Chat session {SessionId} stored in memory: {Status}.", session.SessionId, status);
        }

        return embedded;
    }

    /// <summary>Generates the whole-session summary; null if there is nothing to summarise or the LLM fails.</summary>
    private async Task<string?> TrySummariseAsync(ChatSession session, CancellationToken ct)
    {
        try
        {
            return await summariser.GenerateSummaryAsync(StoredConversation.FromChatSession(session, summary: null), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Embed without a summary rather than not at all; the embedding text still covers the messages.
            logger.LogWarning(ex, "Failed to summarise chat session {SessionId}; embedding it without a summary.", session.SessionId);
            return null;
        }
    }
}
