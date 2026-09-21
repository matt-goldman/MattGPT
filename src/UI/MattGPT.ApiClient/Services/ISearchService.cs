using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>API client for conversation search.</summary>
public interface ISearchService
{
    /// <summary>
    /// Searches imported conversations and returns the top results, ordered best match first.
    /// </summary>
    /// <param name="query">What to search for. The wording that works best depends on
    /// <paramref name="mode"/>: prose for semantic search, distinctive keywords for keyword search.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="mode">Whether to match by meaning or by literal words.</param>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit = 20,
        SearchMode mode = SearchMode.Semantic,
        CancellationToken cancellationToken = default);
}
