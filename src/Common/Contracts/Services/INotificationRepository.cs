using MattGPT.Contracts.Models;

namespace MattGPT.Contracts.Services;

/// <summary>Persistence for <see cref="Notification"/>s, scoped per user.</summary>
public interface INotificationRepository
{
    /// <summary>Store a new notification.</summary>
    Task CreateAsync(Notification notification, CancellationToken ct = default);

    /// <summary>Return the user's most recent notifications, newest first.</summary>
    Task<List<Notification>> ListRecentAsync(string? userId, int limit, CancellationToken ct = default);

    /// <summary>Count the user's unread notifications.</summary>
    Task<long> CountUnreadAsync(string? userId, CancellationToken ct = default);

    /// <summary>Mark the user's notifications read: those in <paramref name="ids"/>, or all of them when it is null.</summary>
    Task MarkReadAsync(string? userId, IReadOnlyCollection<string>? ids, CancellationToken ct = default);
}
