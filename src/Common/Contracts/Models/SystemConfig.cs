namespace MattGPT.Contracts.Models;

/// <summary>
/// Stores the runtime-editable system configuration, including the LLM system prompt.
/// A single document is maintained per application instance.
/// </summary>
public class SystemConfig
{
    /// <summary>Fixed document ID — only one config document is maintained.</summary>
    public string Id { get; set; } = "system-config";

    /// <summary>The system prompt sent to the LLM on every chat request. Null means use the default.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>UTC timestamp when this config was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
