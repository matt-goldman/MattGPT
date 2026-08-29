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
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit = 20,
        SearchMode mode = SearchMode.Semantic,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var url = BuildUrl(query, limit, mode);

        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync(url, cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<List<SearchResult>>(JsonOptions, cancellationToken)
                    ?? [];
            }
            return [];
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SearchResult>>(JsonOptions, cancellationToken)
            ?? [];
    }

    /// <summary>
    /// Builds the request URL. The mode parameter is only sent for keyword search, so the
    /// request stays identical to the pre-toggle one when searching semantically.
    /// </summary>
    private static string BuildUrl(string query, int limit, SearchMode mode)
    {
        var url = $"/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        return mode == SearchMode.Keyword ? $"{url}&mode=keyword" : url;
    }
}
