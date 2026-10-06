using System.Text.Json.Serialization;

namespace MattGPT.Contracts.Models;

/// <summary>What a <see cref="Notification"/> is about.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationKind
{
    SummaryReady,
    SummaryFailed,
    RecordReady,
    RecordFailed,
    ImportCompleted,
    ImportFailed,
    EmbeddingCompleted,
    EmbeddingFailed,
}

/// <summary>
/// An in-app notification telling a user that background work has finished. Persisted, so work that
/// finishes while the user is away still shows as unread.
/// </summary>
public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The recipient, or null for the single anonymous user when auth is off.</summary>
    public string? UserId { get; set; }

    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>App-relative link to what the notification is about (e.g. <c>/chat/conversation/{id}</c>), if any.</summary>
    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRead { get; set; }
}
