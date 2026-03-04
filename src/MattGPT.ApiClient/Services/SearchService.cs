using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="ISearchService"/>
public sealed class SearchService(IHttpClientFactory factory) : ISearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<List<SearchResult>>(
            $"/search?q={Uri.EscapeDataString(query)}&limit={limit}", JsonOptions, cancellationToken)
            ?? [];
    }
}
