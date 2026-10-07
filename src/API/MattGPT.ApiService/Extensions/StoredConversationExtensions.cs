using System.Text;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Extensions;

/// <summary>
/// Plain-text renderings of a <see cref="StoredConversation"/>, for embedding and for
/// including in an LLM prompt.
/// </summary>
public static class StoredConversationExtensions
{
    /// <summary>
    /// Default maximum number of message characters in a <see cref="ToExcerpt"/>. Prevents a
    /// single long conversation from consuming the entire context budget.
    /// </summary>
    public const int DefaultExcerptChars = 4_000;

    /// <summary>
    /// Whether a message contributes to embedding and chunking. Hidden and zero-weight messages are
    /// system scaffolding (custom instructions and the like), and messages with no text contribute
    /// nothing, so the embedding window is dedicated to actual conversational content.
    /// </summary>
    public static bool IsEmbeddable(this StoredMessage message)
        => message.IsConversational
        && !string.IsNullOrWhiteSpace(string.Join(" ", message.Parts));

    /// <param name="conversation">The conversation to render.</param>
    extension(StoredConversation conversation)
    {
        /// <summary>
        /// Builds the text that is embedded for this conversation. Uses the summary if available
        /// (higher quality), but always includes the title and message content so that freshly
        /// imported conversations can be embedded without waiting for LLM summarisation. Message
        /// content is included up to <paramref name="maxChars"/> characters.
        /// </summary>
        /// <remarks>
        /// Changing this output changes the vectors produced for new embeddings, so existing
        /// stored vectors would no longer match on the same terms until conversations are re-embedded.
        /// </remarks>
        public string ToEmbeddingText(int maxChars)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(conversation.Title))
                sb.Append("Title: ").AppendLine(conversation.Title);

            if (!string.IsNullOrWhiteSpace(conversation.Summary))
                sb.Append("Summary: ").AppendLine(conversation.Summary);

            AppendMessageLines(sb, conversation.LinearisedMessages, 0, conversation.LinearisedMessages.Count - 1, maxChars);

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Builds the embeddable text for a range of this conversation's messages — the same rendering
        /// <see cref="ToEmbeddingText"/> uses, without the title and digest, so a chunk's body is
        /// comparable with a whole conversation's.
        /// </summary>
        /// <param name="startIndex">Index of the first message in the range.</param>
        /// <param name="endIndex">Index of the last message in the range (inclusive).</param>
        /// <param name="maxChars">Maximum characters to render.</param>
        public string ToRangeEmbeddingText(int startIndex, int endIndex, int maxChars)
        {
            var sb = new StringBuilder();
            AppendMessageLines(sb, conversation.LinearisedMessages, startIndex, endIndex, maxChars);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Builds a human-readable excerpt of a range of this conversation's messages, for including in
        /// an LLM prompt as the verbatim grounding behind a citation. Unlike <see cref="ToExcerpt"/>,
        /// which renders the conversation from the beginning, this renders exactly the messages a
        /// retrieved chunk covers.
        /// </summary>
        /// <param name="startIndex">Index of the first message in the range.</param>
        /// <param name="endIndex">Index of the last message in the range (inclusive).</param>
        /// <param name="maxChars">Maximum characters to render.</param>
        public string ToRangeExcerpt(int startIndex, int endIndex, int maxChars = DefaultExcerptChars)
        {
            var messages = conversation.LinearisedMessages;
            var from = Math.Max(0, startIndex);
            var to = Math.Min(endIndex, messages.Count - 1);

            var sb = new StringBuilder();

            for (var i = from; i <= to; i++)
            {
                var msg = messages[i];
                if (!msg.IsEmbeddable())
                    continue;

                var line = $"{DisplayRole(msg.Role)}: {string.Join(" ", msg.Parts)}";

                if (sb.Length + line.Length + 1 > maxChars)
                {
                    var remaining = maxChars - sb.Length - 20;
                    if (remaining > 0)
                        sb.AppendLine(line[..remaining] + "...");
                    sb.AppendLine("[excerpt truncated]");
                    break;
                }

                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds a truncated human-readable excerpt of this conversation's messages, limited to
        /// <paramref name="maxChars"/> characters, for including in an LLM prompt or tool result.
        /// </summary>
        public string ToExcerpt(int maxChars = DefaultExcerptChars)
        {
            var sb = new StringBuilder();

            // Only dialogue reaches the model. Tool traffic and scaffolding are excluded here as
            // well as in ToEmbeddingText, so a conversation retrieved as memory reads the same way
            // it was indexed.
            foreach (var msg in conversation.ConversationalMessages)
            {
                var role = DisplayRole(msg.Role);

                var content = string.Join(" ", msg.Parts);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var line = $"{role}: {content}";

                if (sb.Length + line.Length + 1 > maxChars)
                {
                    // Truncate and signal there's more.
                    var remaining = maxChars - sb.Length - 20;
                    if (remaining > 0)
                        sb.AppendLine(line[..remaining] + "...");
                    sb.AppendLine("[conversation truncated]");
                    break;
                }

                sb.AppendLine(line);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Appends <c>role: content</c> lines (plus citation context) for the messages in
    /// <paramref name="startIndex"/>..<paramref name="endIndex"/> inclusive, stopping at
    /// <paramref name="maxChars"/>. Shared by the whole-conversation and per-range embedding
    /// renderings so a chunk is embedded in the same shape as a conversation.
    /// </summary>
    private static void AppendMessageLines(
        StringBuilder sb, List<StoredMessage> messages, int startIndex, int endIndex, int maxChars)
    {
        var from = Math.Max(0, startIndex);
        var to = Math.Min(endIndex, messages.Count - 1);

        for (var i = from; i <= to; i++)
        {
            var msg = messages[i];
            if (!msg.IsEmbeddable())
                continue;

            var content = string.Join(" ", msg.Parts);

            var line = $"{msg.Role}: {content}\n";

            if (sb.Length + line.Length > maxChars)
            {
                // Fit as much as we can.
                var remaining = maxChars - sb.Length;
                if (remaining > 20)
                    sb.Append(line.AsSpan(0, remaining));
                break;
            }

            sb.Append(line);

            // Include citation context (file names and web source titles/URLs).
            if (msg.Citations is { Count: > 0 })
            {
                foreach (var citation in msg.Citations)
                {
                    var citName = !string.IsNullOrWhiteSpace(citation.Name) ? citation.Name
                        : citation.Source;
                    if (citName is not null)
                    {
                        var citLine = $"[Cited: {citName}]\n";
                        if (sb.Length + citLine.Length <= maxChars)
                            sb.Append(citLine);
                    }
                }
            }
        }
    }

    /// <summary>The label a role is rendered under in a human-readable excerpt.</summary>
    private static string DisplayRole(string role) => role switch
    {
        "user" => "User",
        "assistant" => "Assistant",
        "system" => "System",
        "tool" => "Tool",
        _ => role,
    };
}
