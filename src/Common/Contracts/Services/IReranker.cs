namespace MattGPT.Contracts.Services;

/// <summary>
/// The relevance of one document to a query, as scored by a <see cref="IReranker"/>.
/// </summary>
/// <param name="Index">Zero-based index of the document in the list passed to <see cref="IReranker.RerankAsync"/>.</param>
/// <param name="Score">Relevance score; higher is more relevant. The scale is model-specific (typically 0.0–1.0).</param>
public record RerankResult(int Index, float Score);

/// <summary>
/// Scores documents by their relevance to a query using a reranking (cross-encoder) model, which
/// reads the query and each document together and so ranks more accurately than comparing embeddings.
/// </summary>
/// <remarks>
/// Microsoft.Extensions.AI has no reranker abstraction yet (one is proposed for a future
/// Microsoft.Extensions.DataRetrieval package, dotnet/extensions#7507). This interface is kept
/// deliberately minimal so it can be swapped for that one if it ships.
/// </remarks>
public interface IReranker
{
    /// <summary>
    /// Scores <paramref name="documents"/> against <paramref name="query"/> and returns at most
    /// <paramref name="topN"/> results, most relevant first.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="documents">The candidate documents' text.</param>
    /// <param name="topN">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query, IReadOnlyList<string> documents, int topN, CancellationToken ct = default);
}
