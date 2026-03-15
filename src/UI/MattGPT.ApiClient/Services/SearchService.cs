using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="ISearchService"/>
public sealed class SearchService(IHttpClientFactory factory, IAuthFailureHandler authFailureHandler) : ISearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync($"/search?q={Uri.EscapeDataString(query)}&limit={limit}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return [];
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SearchResult>>(JsonOptions, cancellationToken)
            ?? [];
    }
}
