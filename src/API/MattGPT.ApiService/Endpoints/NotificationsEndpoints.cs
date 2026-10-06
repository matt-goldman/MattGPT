using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Endpoints;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        // The current user's recent notifications and unread count. Polled by the web app's bell.
        app.MapGet("/notifications", async (
            INotificationRepository repository, ICurrentUserService currentUser, CancellationToken ct, int limit = 20) =>
        {
            if (limit is < 1 or > 100) limit = 20;

            var items = await repository.ListRecentAsync(currentUser.UserId, limit, ct);
            var unread = await repository.CountUnreadAsync(currentUser.UserId, ct);

            return Results.Ok(new
            {
                unreadCount = unread,
                items = items.Select(n => new
                {
                    id          = n.Id,
                    kind        = n.Kind.ToString(),
                    title       = n.Title,
                    message     = n.Message,
                    link        = n.Link,
                    createdAt   = n.CreatedAt,
                    isRead      = n.IsRead,
                }),
            });
        })
        .WithName("GetNotifications");

        // Mark the given notifications read, or all of them when no ids are sent.
        app.MapPost("/notifications/read", async (
            MarkNotificationsReadRequest? request, INotificationRepository repository, ICurrentUserService currentUser, CancellationToken ct) =>
        {
            await repository.MarkReadAsync(currentUser.UserId, request?.Ids is { Count: > 0 } ids ? ids : null, ct);
            return Results.NoContent();
        })
        .WithName("MarkNotificationsRead");

        return app;
    }
}

/// <summary>Request body for marking notifications read; null or empty <paramref name="Ids"/> means all.</summary>
public record MarkNotificationsReadRequest(List<string>? Ids);
