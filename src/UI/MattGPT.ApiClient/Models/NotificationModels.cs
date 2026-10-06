namespace MattGPT.ApiClient.Models;

/// <summary>An in-app notification about finished background work.</summary>
/// <param name="Kind">e.g. RecordReady, SummaryReady, ImportCompleted, ImportFailed.</param>
/// <param name="Link">App-relative link to what it is about, if any.</param>
public record NotificationItem(string Id, string Kind, string Title, string Message, string? Link, DateTimeOffset CreatedAt, bool IsRead)
{
    /// <summary>Whether the notification reports a failure, for styling.</summary>
    public bool IsFailure => Kind.EndsWith("Failed", StringComparison.Ordinal);
}

/// <summary>The current user's recent notifications and how many are unread.</summary>
public record NotificationsResponse(long UnreadCount, List<NotificationItem> Items);
