using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Raises in-app notifications from background work. Never throws: a notification that can't be
/// stored is logged, and the work it reports on is unaffected.
/// </summary>
public class NotificationPublisher(INotificationRepository repository, ILogger<NotificationPublisher> logger)
{
    public async Task PublishAsync(
        string? userId, NotificationKind kind, string title, string message, string? link = null,
        CancellationToken ct = default)
    {
        try
        {
            await repository.CreateAsync(new Notification
            {
                UserId  = userId,
                Kind    = kind,
                Title   = title,
                Message = message,
                Link    = link,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not store {Kind} notification \"{Title}\".", kind, title);
        }
    }

    /// <summary>The app link for a conversation, used by notifications about it.</summary>
    public static string ConversationLink(string conversationId) => $"/chat/conversation/{Uri.EscapeDataString(conversationId)}";
}
