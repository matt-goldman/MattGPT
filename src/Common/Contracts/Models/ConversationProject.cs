namespace MattGPT.Contracts.Models;

/// <summary>
/// Represents a ChatGPT "Project" — a group of conversations sharing the same
/// <see cref="StoredConversation.ConversationTemplateId"/> where <c>GizmoType</c> is "snorlax".
/// </summary>
public class ConversationProject
{
    /// <summary>The shared template/gizmo ID for this project group.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Number of conversations in this project.</summary>
    public int ConversationCount { get; set; }

    /// <summary>Title of the most recently updated conversation (used as a fallback display name).</summary>
    public string? MostRecentTitle { get; set; }

    /// <summary>Unix timestamp of the most recently updated conversation in the project.</summary>
    public double? LatestUpdateTime { get; set; }

    /// <summary>Unix timestamp of the earliest conversation in the project.</summary>
    public double? EarliestCreateTime { get; set; }
}
