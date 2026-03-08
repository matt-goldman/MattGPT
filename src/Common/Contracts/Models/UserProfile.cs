namespace MattGPT.Contracts.Models;

/// <summary>
/// Stores the user's custom instructions extracted from ChatGPT's user_editable_context messages.
/// A single document is maintained rather than per-conversation copies.
/// </summary>
public class UserProfile
{
    /// <summary>Fixed document ID — only one profile document is maintained.</summary>
    public string Id { get; set; } = "user-profile";

    /// <summary>The "About me" section from ChatGPT custom instructions.</summary>
    public string? UserProfileText { get; set; }

    /// <summary>The "How should ChatGPT respond" section from ChatGPT custom instructions.</summary>
    public string? UserInstructions { get; set; }

    /// <summary>Unix timestamp of the source message from which this profile was extracted.</summary>
    public double? SourceCreateTime { get; set; }

    /// <summary>UTC timestamp when this profile was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
