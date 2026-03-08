namespace MattGPT.ApiClient.Models;

/// <summary>Summary of a chat session returned by the list endpoint.</summary>
public record ChatSessionItem(Guid SessionId, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Status);

/// <summary>Full detail of a chat session including all messages.</summary>
public record SessionDetail(Guid SessionId, string? Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Status, string? RollingSummary, List<SessionMessage> Messages);

/// <summary>A single message within a chat session.</summary>
public record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);

/// <summary>Full detail of an imported conversation including all messages.</summary>
public record ImportedConversationDetail(string ConversationId, string? Title, string? Summary, double? CreateTime, double? UpdateTime, List<ImportedMessage> Messages);

/// <summary>A single message from an imported conversation.</summary>
public record ImportedMessage(string Role, string Content, double? CreateTime);

/// <summary>A RAG source citation included in a chat response.</summary>
public record ChatSource(string ConversationId, string? Title, string? Summary, float Score);

// ── SSE stream events ──────────────────────────────────────────────────────

/// <summary>Base type for all events produced by the chat stream.</summary>
public abstract record ChatStreamEvent;

/// <summary>Carries the session ID assigned by the server for this conversation.</summary>
public sealed record SessionChatEvent(Guid SessionId) : ChatStreamEvent;

/// <summary>A single streamed token of the assistant's response.</summary>
public sealed record TokenChatEvent(string Token) : ChatStreamEvent;

/// <summary>Signals that the LLM has started invoking a tool.</summary>
public sealed record ToolStartChatEvent(string? Tool) : ChatStreamEvent;

/// <summary>Signals that the LLM has finished invoking a tool.</summary>
public sealed record ToolEndChatEvent() : ChatStreamEvent;

/// <summary>Carries the RAG source citations for the completed response.</summary>
public sealed record SourcesChatEvent(IReadOnlyList<ChatSource> Sources) : ChatStreamEvent;

/// <summary>Signals that the stream is complete.</summary>
public sealed record DoneChatEvent() : ChatStreamEvent;
