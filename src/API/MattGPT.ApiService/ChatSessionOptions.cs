namespace MattGPT.ApiService;

/// <summary>
/// Configuration options for multi-turn chat session management and rolling summaries.
/// Bound from the <c>"Chat"</c> configuration section.
/// </summary>
public class ChatSessionOptions
{
    public const string SectionName = "Chat";

    /// <summary>
    /// Maximum estimated token budget for conversation history in the prompt.
    /// Tokens are estimated as character count ÷ 4.
    /// When exceeded, older messages are compressed into a rolling summary.
    /// </summary>
    public int MaxConversationTokens { get; set; } = 2048;

    /// <summary>
    /// Number of recent messages to always include verbatim in the prompt,
    /// regardless of the token budget.
    /// </summary>
    public int RecentMessageCount { get; set; } = 6;

    /// <summary>
    /// System prompt used to instruct the LLM when generating a rolling summary
    /// of older conversation messages.
    /// </summary>
    public string SummaryPrompt { get; set; } =
        "Summarise the conversation so far, preserving key facts, decisions, user preferences, and open questions. Be concise — aim for 200 words or fewer.";
}
