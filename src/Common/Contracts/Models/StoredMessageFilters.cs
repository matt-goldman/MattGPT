namespace MattGPT.Contracts.Models;

/// <summary>
/// The single definition of which stored messages are conversational content. Shared by every
/// consumer that renders a conversation for embedding, for a digest, for a prompt, for search, or
/// for the conversation view.
/// </summary>
/// <remarks>
/// <para>
/// The rule, stated positively: <b>only messages the user sent to the assistant, or the assistant
/// sent to the user, are conversational.</b> Everything else a ChatGPT export contains — tool
/// exchanges, system scaffolding and reasoning traces — is kept on import but never rendered.
/// </para>
/// <para>
/// This matters because an export interleaves the dialogue with a large volume of tool traffic:
/// the text of every file uploaded to a conversation or project (<c>file_search</c>,
/// <c>myfiles_browser</c>), every web fetch (<c>browser</c>, <c>web.run</c>), Python output, image
/// generation and canvas edits. In the reference export
/// (<c>docs/TechnicalReference/export-analysis.md</c>) that is 11,719 <c>tool</c> and 9,035
/// <c>system</c> messages out of 79,910 — 26% by count and far more by character count, since a
/// single <c>file_search</c> result can be a whole document.
/// </para>
/// <para>
/// Two fields are needed, because each misses what the other catches:
/// </para>
/// <list type="bullet">
/// <item><see cref="StoredMessage.Role"/> excludes tool results and system scaffolding. It is the
/// primary discriminator.</item>
/// <item><see cref="StoredMessage.Recipient"/> excludes the other half of a tool exchange: an
/// <c>assistant</c> message addressed to <c>python</c> or <c>bio</c> is a tool call, not a reply to
/// the user, and the role alone cannot tell them apart.</item>
/// </list>
/// <para>
/// <see cref="StoredMessage.Weight"/> and <see cref="StoredMessage.IsHidden"/> are kept as
/// secondary guards, but they are not sufficient on their own and never were: only 7,884 of 79,910
/// messages carry <c>weight: 0.0</c>, so most tool traffic carries <c>weight: 1.0</c> and no hidden
/// flag and passes a weight/hidden filter untouched.
/// </para>
/// </remarks>
public static class StoredMessageFilters
{
    /// <summary>The author role for a message written by the user.</summary>
    public const string UserRole = "user";

    /// <summary>The author role for a message written by the assistant.</summary>
    public const string AssistantRole = "assistant";

    /// <summary>
    /// The <see cref="StoredMessage.Recipient"/> value meaning "the other party in the
    /// conversation" — the user, or the assistant replying to them. Any other value is a tool name.
    /// </summary>
    public const string DialogueRecipient = "all";

    /// <summary>
    /// Content types holding the assistant's private reasoning rather than anything it said to the
    /// user. Excluded: the reasoning is not the answer, and embedding it dilutes retrieval.
    /// </summary>
    private static readonly string[] ReasoningContentTypes = ["thoughts", "reasoning_recap"];

    extension(StoredMessage message)
    {
        /// <summary>
        /// Whether this message is dialogue — sent by the user to the assistant, or by the
        /// assistant to the user.
        /// </summary>
        /// <remarks>
        /// Roles and recipients are matched exactly: the export schema pins the role to a lowercase
        /// enum, and <see cref="StoredConversation.FromChatSession"/> writes the same lowercase
        /// literals. A null <see cref="StoredMessage.Recipient"/> counts as dialogue, so documents
        /// imported before that field was captured keep working and fall back to the role check,
        /// rather than every message being filtered out.
        /// </remarks>
        public bool IsConversational
            => message.Role is UserRole or AssistantRole
               && message.Recipient is null or DialogueRecipient
               && !message.IsReasoning
               && !message.IsHidden
               && message.Weight != 0.0;

        /// <summary>Whether this message is an assistant reasoning trace rather than a reply.</summary>
        public bool IsReasoning => ReasoningContentTypes.Contains(message.ContentType);
    }

    extension(StoredConversation conversation)
    {
        /// <summary>
        /// This conversation's dialogue in chronological order, excluding tool exchanges,
        /// scaffolding and reasoning traces. The preferred way to read a conversation for anything
        /// a model or a user sees.
        /// </summary>
        public IEnumerable<StoredMessage> ConversationalMessages
            => conversation.LinearisedMessages.Where(m => m.IsConversational);
    }
}
