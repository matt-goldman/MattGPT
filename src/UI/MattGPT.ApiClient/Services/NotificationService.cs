using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="INotificationService"/>
public sealed class NotificationService(IHttpClientFactory factory, IAuthFailureHandler authFailureHandler) : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<NotificationsResponse?> GetNotificationsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var url = $"/notifications?limit={limit}";
        var client = CreateClient();
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (!await authFailureHandler.HandleAsync(cancellationToken)) return null;
            using var retry = await client.GetAsync(url, cancellationToken);
            if (!retry.IsSuccessStatusCode) return null;
            return await retry.Content.ReadFromJsonAsync<NotificationsResponse>(JsonOptions, cancellationToken);
        }
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<NotificationsResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkReadAsync(IReadOnlyCollection<string>? ids = null, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var body = new { ids };
        using var response = await client.PostAsJsonAsync("/notifications/read", body, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await authFailureHandler.HandleAsync(cancellationToken))
        {
            using var retry = await client.PostAsJsonAsync("/notifications/read", body, cancellationToken);
        }
    }
}
