using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>API client for semantic conversation search.</summary>
public interface ISearchService
{
    /// <summary>
    /// Runs a semantic search over imported conversations and returns the top results.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default);
}
