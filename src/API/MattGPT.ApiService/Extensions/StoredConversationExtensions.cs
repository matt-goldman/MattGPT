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

            // Append message content up to the character limit.
            // Skip hidden and zero-weight messages (system scaffolding, custom instructions)
            // to dedicate the embedding window to actual conversational content.
            foreach (var msg in conversation.LinearisedMessages)
            {
                if (msg.IsHidden || msg.Weight == 0.0)
                    continue;

                var content = string.Join(" ", msg.Parts);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

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

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Builds a truncated human-readable excerpt of this conversation's messages, limited to
        /// <paramref name="maxChars"/> characters, for including in an LLM prompt or tool result.
        /// </summary>
        public string ToExcerpt(int maxChars = DefaultExcerptChars)
        {
            var sb = new StringBuilder();
            foreach (var msg in conversation.LinearisedMessages)
            {
                var role = msg.Role switch
                {
                    "user" => "User",
                    "assistant" => "Assistant",
                    "system" => "System",
                    "tool" => "Tool",
                    _ => msg.Role,
                };

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
}
