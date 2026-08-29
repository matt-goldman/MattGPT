using System.Text;

namespace MattGPT.Contracts.Models;

/// <summary>
/// A single hit from a keyword / full-text search over stored conversations.
/// </summary>
/// <param name="ConversationId">The matched conversation's id.</param>
/// <param name="Score">
/// Backend-specific relevance rank (MongoDB <c>textScore</c>, Postgres <c>ts_rank_cd</c>).
/// Only meaningful for ordering results within a single result set — the scales differ
/// between backends and are NOT comparable to the cosine similarity from a vector search.
/// </param>
/// <param name="Title">The conversation title, if any.</param>
/// <param name="Summary">The stored summary, if the conversation has been summarised.</param>
/// <param name="Snippets">Short excerpts of the conversation text around the matched terms.</param>
public record ConversationTextSearchResult(
    string ConversationId,
    double Score,
    string? Title,
    string? Summary,
    IReadOnlyList<string> Snippets);

/// <summary>
/// Rescales the backend-specific text ranks on <see cref="ConversationTextSearchResult.Score"/>
/// into a 0-1 range relative to the best hit in the same result set.
/// </summary>
/// <remarks>
/// MongoDB textScore and Postgres ts_rank_cd are on unrelated, unbounded scales, so a raw rank
/// is meaningless to anything downstream: it cannot be rendered as a percentage, and it cannot
/// be compared with the 0-1 cosine similarities that keyword hits get merged with. Relative
/// scoring keeps the ordering faithful while making the number mean one specific thing -
/// "how strong compared with the best keyword hit here" - whichever backend produced it.
/// </remarks>
public static class ConversationTextSearchRanking
{
    /// <summary>Returns <paramref name="score"/> as a fraction of <paramref name="maxScore"/>.</summary>
    public static float Relative(double score, double maxScore)
        => maxScore > 0 ? (float)(score / maxScore) : 0f;

    /// <summary>Returns the highest rank in <paramref name="results"/>, or 0 when empty.</summary>
    public static double MaxScore(IReadOnlyList<ConversationTextSearchResult> results)
        => results.Count > 0 ? results.Max(r => r.Score) : 0d;
}

/// <summary>
/// Builds "keyword in context" snippets for full-text search hits.
/// </summary>
/// <remarks>
/// Snippet extraction lives here rather than in the database layer because neither backend
/// gives us portable highlighting: Postgres has <c>ts_headline</c>, MongoDB has no equivalent.
/// Doing it in memory over the already-fetched document keeps snippets identical whichever
/// repository implementation is in use. Matching here is deliberately simpler than the
/// database's (plain case-insensitive substring, no stemming), so a stemmed database match
/// can occasionally yield no snippet — callers should fall back to the summary.
/// </remarks>
public static class ConversationTextSnippets
{
    /// <summary>Characters of context included either side of a matched term.</summary>
    private const int ContextChars = 100;

    /// <summary>Terms shorter than this are ignored when locating matches.</summary>
    private const int MinTermLength = 3;

    /// <summary>
    /// Extracts up to <paramref name="maxSnippets"/> excerpts from <paramref name="conversation"/>
    /// centred on occurrences of the terms in <paramref name="query"/>.
    /// </summary>
    public static IReadOnlyList<string> Build(StoredConversation conversation, string query, int maxSnippets = 3)
    {
        var terms = ExtractTerms(query);
        if (terms.Count == 0 || maxSnippets <= 0)
            return [];

        var snippets = new List<string>();

        foreach (var msg in conversation.LinearisedMessages)
        {
            if (msg.IsHidden || msg.Weight == 0.0)
                continue;

            var content = string.Join(" ", msg.Parts);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var index = FindFirstMatch(content, terms);
            if (index < 0)
                continue;

            var role = msg.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                _ => msg.Role,
            };

            snippets.Add($"{role}: {Excerpt(content, index)}");

            if (snippets.Count == maxSnippets)
                break;
        }

        return snippets;
    }

    /// <summary>
    /// Splits a search query into the bare words used for snippet location, dropping the
    /// query syntax (quotes, leading <c>-</c> exclusions) and very short words.
    /// </summary>
    private static List<string> ExtractTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return [.. query
            .Split([' ', '\t', '\n', '\r', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.StartsWith('-'))
            .Select(t => t.Trim().Trim(',', '.', '?', '!', ':', ';', '(', ')'))
            .Where(t => t.Length >= MinTermLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Returns the index of the earliest occurrence of any term, or -1 if none match.</summary>
    private static int FindFirstMatch(string content, List<string> terms)
    {
        var best = -1;
        foreach (var term in terms)
        {
            var idx = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (best < 0 || idx < best))
                best = idx;
        }
        return best;
    }

    /// <summary>Cuts a window of context around <paramref name="matchIndex"/>, with ellipses where truncated.</summary>
    private static string Excerpt(string content, int matchIndex)
    {
        var start = Math.Max(0, matchIndex - ContextChars);
        var end = Math.Min(content.Length, matchIndex + ContextChars);

        var sb = new StringBuilder();
        if (start > 0) sb.Append("...");
        sb.Append(content.AsSpan(start, end - start).Trim());
        if (end < content.Length) sb.Append("...");

        return sb.ToString();
    }
}
