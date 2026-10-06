using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>API client for the current user's in-app notifications.</summary>
public interface INotificationService
{
    /// <summary>Returns recent notifications and the unread count, or null if they couldn't be fetched.</summary>
    Task<NotificationsResponse?> GetNotificationsAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>Marks the given notifications read, or all of them when <paramref name="ids"/> is null.</summary>
    Task MarkReadAsync(IReadOnlyCollection<string>? ids = null, CancellationToken cancellationToken = default);
}
