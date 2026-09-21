namespace MattGPT.ApiClient.Models;

/// <summary>How the search endpoint should match the query against stored conversations.</summary>
public enum SearchMode
{
    /// <summary>
    /// Vector similarity over conversation embeddings: matches by meaning, so it finds
    /// conversations about the topic even when they use entirely different words.
    /// </summary>
    Semantic,

    /// <summary>
    /// Database full-text search: matches the literal words typed (case-insensitive and
    /// lightly stemmed), so it finds exact names, identifiers, and error codes that carry
    /// too little meaning for the embedding to retrieve.
    /// </summary>
    Keyword,
}

/// <summary>A single search result.</summary>
/// <param name="ConversationId">Id of the matching conversation.</param>
/// <param name="Score">
/// Relevance, 0–1. In <see cref="SearchMode.Semantic"/> this is the cosine similarity and is
/// meaningful on its own; in <see cref="SearchMode.Keyword"/> it is a rank relative to the best
/// hit in the same result set, so it should be used for ordering rather than shown as a figure.
/// </param>
/// <param name="Title">Conversation title, if any.</param>
/// <param name="Summary">Stored summary, if the conversation has been summarised.</param>
/// <param name="Snippets">
/// Excerpts around the matched words. Only populated for <see cref="SearchMode.Keyword"/>,
/// where showing where the term actually appears is more useful than a relevance figure.
/// </param>
public record SearchResult(
    string ConversationId,
    float Score,
    string? Title,
    string? Summary,
    IReadOnlyList<string>? Snippets = null)
{
    /// <summary>
    /// First matched excerpt, or <c>null</c> when there is none. Convenience for XAML bindings,
    /// which cannot index into a collection.
    /// </summary>
    public string? TopSnippet => Snippets is { Count: > 0 } ? Snippets[0] : null;
}
