using System.Text.Json;
using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Streams a ChatGPT <c>conversations.json</c> export and yields each conversation
/// as a <see cref="ParsedConversation"/> with its message tree linearised to the active thread.
/// </summary>
public class ConversationParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Asynchronously streams conversations from the given JSON stream.
    /// Each conversation's message tree is linearised to the active thread before being yielded.
    /// </summary>
    /// <param name="stream">A readable stream containing the ChatGPT export JSON array.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An async enumerable of parsed conversations.</returns>
    public async IAsyncEnumerable<ParsedConversation> ParseAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversations = JsonSerializer.DeserializeAsyncEnumerable<Conversation>(
            stream,
            JsonOptions,
            cancellationToken);

        await foreach (var conversation in conversations.WithCancellation(cancellationToken))
        {
            if (conversation is null)
                continue;

            yield return new ParsedConversation
            {
                Id = conversation.Id,
                Title = conversation.Title,
                CreateTime = conversation.CreateTime,
                UpdateTime = conversation.UpdateTime,
                DefaultModelSlug = conversation.DefaultModelSlug,
                GizmoId = conversation.GizmoId,
                GizmoType = conversation.GizmoType,
                ConversationTemplateId = conversation.ConversationTemplateId,
                IsDoNotRemember = conversation.IsDoNotRemember,
                MemoryScope = conversation.MemoryScope,
                IsArchived = conversation.IsArchived,
                Messages = Linearise(conversation),
            };
        }
    }

    /// <summary>
    /// Linearises the message tree of a conversation to produce the active thread.
    /// Starts at <c>current_node</c>, walks parent pointers to the root, then reverses.
    /// Only nodes that have an actual message are included.
    /// </summary>
    public static List<Message> Linearise(Conversation conversation)
    {
        if (conversation.Mapping.Count == 0 || conversation.CurrentNode is null)
            return new List<Message>();

        var result = new List<Message>();
        var currentId = conversation.CurrentNode;

        while (currentId is not null && conversation.Mapping.TryGetValue(currentId, out var node))
        {
            if (node.Message is not null)
                result.Add(node.Message);

            currentId = node.Parent;
        }

        result.Reverse();
        return result;
    }
}
