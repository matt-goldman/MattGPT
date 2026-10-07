namespace MattGPT.Contracts.Models;

/// <summary>
/// The single definition of which stored messages are conversational content. Shared by every
/// consumer that renders a conversation for embedding, for a digest, for a prompt, for search, or
/// for the conversation view.
/// </summary>
/// <remarks>
/// <para>
/// A ChatGPT export interleaves the dialogue with tool traffic: <c>file_search</c> and
/// <c>myfiles_browser</c> results (every file ever uploaded to a conversation or project),
/// <c>browser</c>/<c>web.run</c> fetches, <c>python</c> output, <c>dalle</c> and <c>canmore</c>
/// calls, and the system scaffolding that drives them. That traffic is deliberately kept on import
/// — it is the only record of which files and sources a conversation involved — but it is not
/// dialogue, it dwarfs the dialogue by volume, and it must never reach an embedding, a digest, a
/// model prompt or the conversation view.
/// </para>
/// <para>
/// <see cref="StoredMessage.Weight"/> and <see cref="StoredMessage.IsHidden"/> are not sufficient
/// on their own. In a real export (see <c>docs/TechnicalReference/export-analysis.md</c>) only
/// 7,884 of 79,910 messages carry <c>weight: 0.0</c>, while there are 11,719 <c>tool</c> and 9,035
/// <c>system</c> messages — so most tool traffic carries <c>weight: 1.0</c> and no hidden flag, and
/// passes a weight/hidden filter untouched. Filtering on the author role is what actually excludes it.
/// </para>
/// </remarks>
public static class StoredMessageFilters
{
    /// <summary>The author role for a message written by the user.</summary>
    public const string UserRole = "user";

    /// <summary>The author role for a message written by the assistant.</summary>
    public const string AssistantRole = "assistant";

    extension(StoredMessage message)
    {
        /// <summary>
        /// Whether this message is dialogue between the user and the assistant, rather than tool
        /// traffic (<c>tool</c>) or scaffolding (<c>system</c>, hidden, or zero-weight).
        /// </summary>
        /// <remarks>
        /// Roles are matched exactly: the export schema pins the role to a lowercase enum, and the
        /// chat-session projection (<see cref="StoredConversation.FromChatSession"/>) writes the
        /// same lowercase literals.
        /// </remarks>
        public bool IsConversational
            => message.Role is UserRole or AssistantRole
               && !message.IsHidden
               && message.Weight != 0.0;
    }

    extension(StoredConversation conversation)
    {
        /// <summary>
        /// This conversation's dialogue in chronological order, excluding tool traffic and
        /// scaffolding. The preferred way to read a conversation for anything a model or a user sees.
        /// </summary>
        public IEnumerable<StoredMessage> ConversationalMessages
            => conversation.LinearisedMessages.Where(m => m.IsConversational);
    }
}
