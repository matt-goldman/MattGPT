using System.Text;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Manages multi-turn chat sessions with rolling summary support.
/// Provides session lifecycle management and handles the summarisation
/// of older messages when the conversation exceeds the configured token budget.
/// </summary>
public class ChatSessionService(
    IChatSessionRepository repository,
    IChatClient chatClient,
    IOptions<ChatSessionOptions> options,
    ICurrentUserService currentUser,
    ILogger<ChatSessionService> logger,
    ChatSessionMemorySignal? memorySignal = null)
{
    /// <summary>Approximate characters per token for estimation (English text).</summary>
    private const int CharsPerToken = 4;
    private readonly ChatSessionOptions _options = options.Value;

    /// <summary>
    /// Retrieves an existing session or creates a new one. Returns the session.
    /// </summary>
    public async Task<ChatSession> GetOrCreateAsync(Guid? sessionId, CancellationToken ct = default)
    {
        if (sessionId.HasValue)
        {
            var existing = await repository.GetByIdAsync(sessionId.Value, ct);
            if (existing is not null)
                return existing;

            logger.LogWarning("Session {SessionId} not found, creating new session.", sessionId.Value);
        }

        var session = new ChatSession { UserId = currentUser.UserId };
        await repository.CreateAsync(session, ct);
        logger.LogInformation("Created new chat session {SessionId}.", session.SessionId);

        await CompletePreviousSessionsAsync(session, ct);

        return session;
    }

    /// <summary>
    /// Starting a new chat completes the user's other active sessions, so they become memory. Only the
    /// status changes here; summarising and embedding happen in the background
    /// (<see cref="ChatSessionLifecycleService"/>), which is woken so it doesn't wait for its next sweep.
    /// </summary>
    private async Task CompletePreviousSessionsAsync(ChatSession newSession, CancellationToken ct)
    {
        try
        {
            var completed = await repository.CompleteOtherActiveSessionsAsync(newSession.UserId, newSession.SessionId, ct);
            if (completed > 0)
            {
                logger.LogInformation("New chat {SessionId} completed {Count} previous session(s).", newSession.SessionId, completed);
                memorySignal?.Notify();
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Don't fail the new chat over this: the idle sweep completes them later.
            logger.LogWarning(ex, "Could not complete previous sessions when starting chat {SessionId}.", newSession.SessionId);
        }
    }

    /// <summary>
    /// Records a user message, triggers rolling summary if needed,
    /// and returns the updated session state for prompt construction.
    /// </summary>
    public async Task<ChatSession> AddUserMessageAsync(
        ChatSession session, string content, CancellationToken ct = default)
    {
        // Continuing a completed session makes it active again. Its summary and embedding status no
        // longer cover the whole session; it is re-summarised and re-embedded when it next completes.
        if (session.Status == ChatSessionStatus.Completed)
        {
            await repository.ReactivateAsync(session.SessionId, ct);
            session.Status = ChatSessionStatus.Active;
            session.Summary = null;
            session.EmbeddingStatus = ChatSessionEmbeddingStatus.None;
            logger.LogInformation("Reactivated completed chat session {SessionId}.", session.SessionId);
        }

        var message = new ChatSessionMessage
        {
            Role = "user",
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
        };

        session.Messages.Add(message);
        await repository.AddMessageAsync(session.SessionId, message, ct);

        // Auto-generate title from the first user message.
        if (session.Title is null)
        {
            session.Title = content.Length > 80 ? content[..80] + "…" : content;
            await repository.UpdateTitleAsync(session.SessionId, session.Title, ct);
        }

        // Check if rolling summary is needed before the LLM call.
        await MaybeUpdateRollingSummaryAsync(session, ct);

        return session;
    }

    /// <summary>
    /// Records an assistant response message in the session, with the past conversations it drew on.
    /// </summary>
    public async Task AddAssistantMessageAsync(
        ChatSession session, string content, IReadOnlyList<ChatSource>? sources = null, CancellationToken ct = default)
    {
        var message = new ChatSessionMessage
        {
            Role = "assistant",
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Sources = sources is { Count: > 0 }
                ? [.. sources.Select(s => new ChatSessionMessageSource
                    {
                        ConversationId = s.ConversationId,
                        Title = s.Title,
                        Summary = s.Summary,
                        Score = s.Score,
                        Source = s.Source,
                    })]
                : null,
        };

        session.Messages.Add(message);
        await repository.AddMessageAsync(session.SessionId, message, ct);
    }

    /// <summary>
    /// Returns the recent messages that should be included verbatim in the prompt,
    /// respecting <see cref="ChatSessionOptions.RecentMessageCount"/>.
    /// </summary>
    public IReadOnlyList<ChatSessionMessage> GetRecentMessages(ChatSession session)
    {
        var count = Math.Min(_options.RecentMessageCount, session.Messages.Count);
        return [.. session.Messages.TakeLast(count)];
    }

    /// <summary>
    /// Estimates token count for a piece of text using the chars ÷ 4 heuristic.
    /// </summary>
    public static int EstimateTokens(string? text)
        => (text?.Length ?? 0) / CharsPerToken;

    /// <summary>
    /// Checks whether the conversation history exceeds the token budget and,
    /// if so, generates or updates the rolling summary by compressing older messages.
    /// </summary>
    private async Task MaybeUpdateRollingSummaryAsync(ChatSession session, CancellationToken ct)
    {
        // Calculate total token cost of all messages.
        var totalTokens = session.Messages.Sum(m => EstimateTokens(m.Content));

        if (totalTokens <= _options.MaxConversationTokens)
        {
            logger.LogDebug(
                "Session {SessionId}: {Tokens} estimated tokens, within budget of {Budget}. No summary needed.",
                session.SessionId, totalTokens, _options.MaxConversationTokens);
            return;
        }

        // Determine which messages fall outside the recent window and need summarising.
        var recentCount = Math.Min(_options.RecentMessageCount, session.Messages.Count);
        var olderMessageCount = session.Messages.Count - recentCount;

        if (olderMessageCount <= 0)
        {
            logger.LogDebug(
                "Session {SessionId}: Over budget but all messages are in the recent window. Cannot summarise.",
                session.SessionId);
            return;
        }

        var olderMessages = session.Messages.Take(olderMessageCount).ToList();

        logger.LogInformation(
            "Session {SessionId}: {Tokens} estimated tokens exceeds budget of {Budget}. " +
            "Summarising {OlderCount} older messages.",
            session.SessionId, totalTokens, _options.MaxConversationTokens, olderMessages.Count);

        var summary = await GenerateRollingSummaryAsync(session.RollingSummary, olderMessages, ct);

        session.RollingSummary = summary;
        await repository.UpdateRollingSummaryAsync(session.SessionId, summary, ct);

        logger.LogInformation(
            "Session {SessionId}: Rolling summary updated ({SummaryLength} chars).",
            session.SessionId, summary.Length);
    }

    /// <summary>
    /// Generates a rolling summary by asking the LLM to compress the prior summary
    /// (if any) plus the older messages into a concise summary.
    /// </summary>
    private async Task<string> GenerateRollingSummaryAsync(
        string? priorSummary,
        IReadOnlyList<ChatSessionMessage> messagesToSummarise,
        CancellationToken ct)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(_options.SummaryPrompt);
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(priorSummary))
        {
            prompt.AppendLine("=== PREVIOUS SUMMARY ===");
            prompt.AppendLine(priorSummary);
            prompt.AppendLine();
        }

        prompt.AppendLine("=== MESSAGES TO INCORPORATE ===");
        foreach (var msg in messagesToSummarise)
        {
            var role = msg.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                _ => msg.Role,
            };
            prompt.AppendLine($"{role}: {msg.Content}");
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt.ToString()),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text;
    }
}
