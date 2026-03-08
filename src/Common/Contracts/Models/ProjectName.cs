namespace MattGPT.Contracts.Models;

/// <summary>
/// Stores a user-assigned display name for a ChatGPT project (identified by its
/// <see cref="StoredConversation.ConversationTemplateId"/>).
/// </summary>
public class ProjectName
{
    /// <summary>The ConversationTemplateId that identifies the project group.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>The user-assigned display name for this project.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the name was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
