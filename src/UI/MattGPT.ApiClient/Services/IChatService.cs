using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>
/// API client for chat operations: starting conversations, streaming responses,
/// and loading session or imported-conversation history.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Streams the assistant response for the given <paramref name="message"/>.
    /// Yields typed <see cref="ChatStreamEvent"/> instances as the server sends SSE frames.
    /// </summary>
    IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(string message, Guid? sessionId, CancellationToken cancellationToken = default);

    /// <summary>Returns the full detail of a chat session, including all messages.</summary>
    Task<SessionDetail?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent chat sessions up to <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ChatSessionItem>> GetSessionsAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Returns the full detail of an imported conversation, including all messages.</summary>
    Task<ImportedConversationDetail?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
