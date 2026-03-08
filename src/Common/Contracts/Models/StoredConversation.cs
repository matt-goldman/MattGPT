using System.Text;
using System.Text.Json;

namespace MattGPT.Contracts.Models;

/// <summary>Processing status of a stored conversation.</summary>
public enum ConversationProcessingStatus { Imported, Summarised, Embedded, SummaryError, EmbeddingError }

/// <summary>A citation stored with a message.</summary>
public class StoredCitation
{
    public int? StartIndex { get; set; }
    public int? EndIndex { get; set; }
    public string? FormatType { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? Text { get; set; }
}

/// <summary>A content reference stored with a message.</summary>
public class StoredContentReference
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? MatchedText { get; set; }
    public string? Snippet { get; set; }
    public string? Url { get; set; }
    public string? Source { get; set; }
}

/// <summary>
/// A single message as stored in MongoDB, with parts normalised to strings.
/// </summary>
public class StoredMessage
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public List<string> Parts { get; set; } = new();
    public double? CreateTime { get; set; }

    /// <summary>Programming language for code content type.</summary>
    public string? Language { get; set; }

    /// <summary>Source URL for tether_quote content type.</summary>
    public string? Url { get; set; }

    /// <summary>Source domain for tether_quote content type.</summary>
    public string? Domain { get; set; }

    /// <summary>Source page title for tether_quote content type.</summary>
    public string? SourceTitle { get; set; }

    /// <summary>
    /// Weight/priority from the ChatGPT export. 0.0 indicates system/scaffolding messages;
    /// 1.0 indicates normal visible messages. Null if not present in the export.
    /// </summary>
    public double? Weight { get; set; }

    /// <summary>
    /// Whether this message should be excluded from embedding and summarisation.
    /// Derived from <c>metadata.is_visually_hidden_from_conversation</c> in the export.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>Citations parsed from message metadata. Null if none were present.</summary>
    public List<StoredCitation>? Citations { get; set; }

    /// <summary>Content references parsed from message metadata (hidden type excluded). Null if none were present.</summary>
    public List<StoredContentReference>? ContentReferences { get; set; }

    private const string HiddenContentReferenceType = "hidden";

    public static StoredMessage From(Message message)
    {
        var stored = new StoredMessage
        {
            Id = message.Id,
            Role = message.Author.Role,
            ContentType = message.Content.ContentType,
            CreateTime = message.CreateTime,
            Weight = message.Weight,
            IsHidden = message.Metadata?.IsVisuallyHiddenFromConversation == true,
        };

        ExtractContent(message.Content, stored);

        if (message.Metadata?.Citations is { Count: > 0 } citations)
        {
            stored.Citations = citations.Select(c => new StoredCitation
            {
                StartIndex = c.StartIndex,
                EndIndex = c.EndIndex,
                FormatType = c.FormatType,
                Type = c.Metadata?.Type,
                Name = c.Metadata?.Title,
                Source = c.Metadata?.Url,
                Text = c.Metadata?.Text,
            }).ToList();
        }

        if (message.Metadata?.ContentReferences is { Count: > 0 } refs)
        {
            var nonHidden = refs.Where(r => r.Type != HiddenContentReferenceType).ToList();
            if (nonHidden.Count > 0)
            {
                stored.ContentReferences = nonHidden.Select(r => new StoredContentReference
                {
                    Type = r.Type,
                    Name = r.Title,
                    MatchedText = r.MatchedText,
                    Snippet = r.Snippet,
                    Url = r.Url,
                    Source = r.Source,
                }).ToList();
            }
        }

        return stored;
    }

    private static void ExtractContent(Content content, StoredMessage stored)
    {
        switch (content.ContentType)
        {
            case "text":
            case "multimodal_text":
                stored.Parts = ExtractParts(content.Parts);
                break;

            case "code":
                stored.Language = content.Language;
                stored.Parts = !string.IsNullOrEmpty(content.Text) ? [content.Text] : [];
                break;

            case "execution_output":
                stored.Parts = !string.IsNullOrEmpty(content.Text) ? [content.Text] : [];
                break;

            case "tether_quote":
                stored.Url = content.Url;
                stored.Domain = content.Domain;
                stored.SourceTitle = content.Title;
                var quoteParts = new List<string>();
                if (!string.IsNullOrEmpty(content.Text))
                    quoteParts.Add(content.Text);
                var sourceLabel = !string.IsNullOrEmpty(content.Title) ? content.Title : content.Domain;
                if (!string.IsNullOrEmpty(sourceLabel))
                    quoteParts.Add($"[Source: {sourceLabel}]");
                stored.Parts = quoteParts;
                break;

            case "tether_browsing_display":
                var browseParts = new List<string>();
                if (!string.IsNullOrEmpty(content.Result))
                    browseParts.Add(content.Result);
                if (!string.IsNullOrEmpty(content.Summary))
                    browseParts.Add(content.Summary);
                stored.Parts = browseParts;
                break;

            case "thoughts":
                stored.Parts = content.Thoughts?
                    .Where(t => !string.IsNullOrEmpty(t.Content))
                    .Select(t => t.Content!)
                    .ToList() ?? [];
                break;

            case "reasoning_recap":
                stored.Parts = !string.IsNullOrEmpty(content.ReasoningContent)
                    ? [content.ReasoningContent] : [];
                break;

            case "user_editable_context":
                var ctxParts = new List<string>();
                if (!string.IsNullOrEmpty(content.UserProfile))
                    ctxParts.Add($"[User Profile] {content.UserProfile}");
                if (!string.IsNullOrEmpty(content.UserInstructions))
                    ctxParts.Add($"[User Instructions] {content.UserInstructions}");
                stored.Parts = ctxParts;
                break;

            case "system_error":
                var errorParts = new List<string>();
                if (!string.IsNullOrEmpty(content.Name))
                    errorParts.Add($"[Error: {content.Name}]");
                if (!string.IsNullOrEmpty(content.Text))
                    errorParts.Add(content.Text);
                stored.Parts = errorParts;
                break;

            case "citable_code_output":
                stored.Parts = !string.IsNullOrEmpty(content.OutputStr)
                    ? [content.OutputStr] : [];
                break;

            case "computer_output":
                var compParts = new List<string>();
                if (content.State is not null)
                {
                    if (!string.IsNullOrEmpty(content.State.Title))
                        compParts.Add(content.State.Title);
                    if (!string.IsNullOrEmpty(content.State.Url))
                        compParts.Add(content.State.Url);
                }
                stored.Parts = compParts;
                break;

            default:
                stored.Parts = ExtractParts(content.Parts);
                break;
        }
    }

    /// <summary>
    /// Extracts parts from a list of JSON elements, converting strings directly
    /// and formatting known object types (e.g. image_asset_pointer) as human-readable placeholders.
    /// </summary>
    internal static List<string> ExtractParts(List<JsonElement>? parts)
    {
        if (parts is null || parts.Count == 0)
            return [];

        return parts.Select(p =>
        {
            if (p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? string.Empty;

            if (p.ValueKind == JsonValueKind.Object)
                return FormatObjectPart(p);

            return p.GetRawText();
        }).ToList();
    }

    private static string FormatObjectPart(JsonElement element)
    {
        if (element.TryGetProperty("content_type", out var ct) &&
            ct.ValueKind == JsonValueKind.String &&
            ct.GetString() == "image_asset_pointer")
        {
            return FormatImageAssetPointer(element);
        }

        return element.GetRawText();
    }

    internal static string FormatImageAssetPointer(JsonElement element)
    {
        var width = element.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number
            ? (int?)w.GetInt32() : null;
        var height = element.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number
            ? (int?)h.GetInt32() : null;
        var isDalle = element.TryGetProperty("dalle", out _);

        var sb = new StringBuilder("[");
        sb.Append(isDalle ? "Image" : "Uploaded image");

        if (width.HasValue && height.HasValue)
        {
            sb.Append(": ").Append(width.Value).Append('×').Append(height.Value);
            if (isDalle)
                sb.Append(", DALL-E generated");
        }
        else if (isDalle)
        {
            sb.Append(": DALL-E generated");
        }

        sb.Append(']');
        return sb.ToString();
    }
}

/// <summary>
/// A parsed ChatGPT conversation stored as a MongoDB document.
/// <c>ConversationId</c> is used as the document <c>_id</c> to guarantee uniqueness.
/// </summary>
public class StoredConversation
{
    /// <summary>The original conversation ID from the ChatGPT export, used as the MongoDB document _id.</summary>
    public string ConversationId { get; set; } = string.Empty;

    public string? Title { get; set; }
    public double? CreateTime { get; set; }
    public double? UpdateTime { get; set; }
    public string? DefaultModelSlug { get; set; }

    /// <summary>Identifies which custom GPT was used (null for standard conversations).</summary>
    public string? GizmoId { get; set; }

    /// <summary>Type of GPT: "gpt" (custom GPT), "snorlax" (project), or null (standard ChatGPT).</summary>
    public string? GizmoType { get; set; }

    /// <summary>Project/template association for the conversation.</summary>
    public string? ConversationTemplateId { get; set; }

    /// <summary>
    /// Whether the user opted out of memory for this conversation.
    /// Conversations with this flag are stored but annotated so downstream consumers can filter them.
    /// </summary>
    public bool? IsDoNotRemember { get; set; }

    /// <summary>Memory scope for this conversation (e.g. "global_enabled", "project_enabled").</summary>
    public string? MemoryScope { get; set; }

    /// <summary>Whether this conversation has been archived.</summary>
    public bool? IsArchived { get; set; }

    /// <summary>Active-thread messages in chronological order, linearised from the message tree.</summary>
    public List<StoredMessage> LinearisedMessages { get; set; } = new();

    /// <summary>UTC timestamp when this document was last imported.</summary>
    public DateTimeOffset ImportTimestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tracks how far through the RAG pipeline this conversation has been processed.</summary>
    public ConversationProcessingStatus ProcessingStatus { get; set; } = ConversationProcessingStatus.Imported;

    /// <summary>LLM-generated summary of this conversation. Populated after summarisation.</summary>
    public string? Summary { get; set; }

    /// <summary>Embedding vector generated from the summary. Populated after embedding generation.</summary>
    public float[]? Embedding { get; set; }

    /// <summary>
    /// The Identity user ID of the owner, or <c>null</c> for data imported/created without authentication.
    /// Used to scope data to individual users when auth is enabled.
    /// </summary>
    public string? UserId { get; set; }

    public static StoredConversation From(ParsedConversation conversation, string? userId = null) => new()
    {
        ConversationId = conversation.Id,
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
        LinearisedMessages = [.. conversation.Messages.Select(StoredMessage.From)],
        ImportTimestamp = DateTimeOffset.UtcNow,
        ProcessingStatus = ConversationProcessingStatus.Imported,
        UserId = userId,
    };
}
