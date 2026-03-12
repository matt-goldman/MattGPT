using CommunityToolkit.Mvvm.ComponentModel;

namespace MattGPT.Mobile.ViewModels;

/// <summary>
/// Represents a single chat message in the UI, with a mutable Content property
/// to support streaming token-by-token assistant responses.
/// </summary>
public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    public required string Role { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsUser => Role == "user";
}
