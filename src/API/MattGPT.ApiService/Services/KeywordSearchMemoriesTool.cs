using System.ComponentModel;
using System.Text;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides the <c>search_memories_keyword</c> tool function that the LLM can invoke to
/// search past conversations by literal keyword, using the database's own full-text index
/// via <see cref="IConversationRepository.SearchTextAsync"/>.
/// </summary>
/// <remarks>
/// This is the lexical counterpart to <see cref="SearchMemoriesTool"/>, which searches the
/// same conversations semantically. The two fail in opposite directions - vector search
/// misses rare tokens (identifiers, error codes, package names) because they carry little
/// meaning in the embedding, while keyword search misses paraphrases - so the descriptions of
/// both tools name the other as the fallback. Keep them in sync when either changes.
/// </remarks>
public class KeywordSearchMemoriesTool(
    IConversationRepository repository,
    IOptions<RagOptions> options,
    ICurrentUserService currentUser,
    ILogger<KeywordSearchMemoriesTool> logger)
{
    /// <summary>The tool name exposed to the LLM and emitted in tool_start/tool_end events.</summary>
    public const string ToolName = "search_memories_keyword";

    private readonly RagOptions _options = options.Value;

    /// <summary>
    /// Sources retrieved by the most recent tool invocation. Populated after
    /// <see cref="SearchMemoriesKeywordAsync"/> is called by the LLM tool-call loop.
    /// </summary>
    public IReadOnlyList<ChatSource> LastSources { get; private set; } = [];

    /// <summary>
    /// Creates an <see cref="AIFunction"/> wrapping <see cref="SearchMemoriesKeywordAsync"/>
    /// that can be passed to <see cref="ChatOptions.Tools"/>.
    /// </summary>
    public AIFunction CreateAIFunction()
    {
        return AIFunctionFactory.Create(
            SearchMemoriesKeywordAsync,
            name: ToolName,
            description: "Keyword (full-text) search over the user's past conversations. " +
                "This matches the LITERAL WORDS in the query against the text of past conversations, " +
                "so it is the right tool for anything that has to match exactly: names, project or product names, " +
                "identifiers, file names, package or API names, error messages and codes, commands, URLs, " +
                "and any wording the user quoted or asked you to find verbatim. " +
                "Query syntax: bare words are matched with basic stemming (so \"binding\" also matches \"bindings\"); " +
                "wrap words in double quotes to require an exact phrase; prefix a word with - to exclude it. " +
                "Keep queries to the few distinctive words that must appear - common words add noise, " +
                "and long natural-language sentences match badly here. " +
                "It will NOT find conversations that discussed the topic in different words: " +
                "for that, use search_memories, which matches by meaning. " +
                "Use search_memories first for topic questions and this tool when you need a specific term, " +
                "or when search_memories returned nothing useful and the user gave you an exact string to look for. " +
                "The results contain excerpts of the matching conversations - use them directly in your answer.");
    }

    /// <summary>
    /// Callback invoked just before the tool search begins, with the tool name.
    /// Used to emit tool_start SSE events in the streaming pipeline.
    /// </summary>
    public Action<string>? OnStarted { get; set; }

    /// <summary>
    /// Callback invoked after the tool search completes, with the tool name.
    /// Used to emit tool_end SSE events in the streaming pipeline.
    /// </summary>
    public Action<string>? OnCompleted { get; set; }

    /// <summary>
    /// Runs a full-text search against the conversation store and formats the matching
    /// excerpts for the LLM. Unlike the semantic tool there is no score threshold to apply:
    /// the database only returns conversations that actually contain the terms, so every
    /// hit is a real match and ranking only decides the order.
    /// </summary>
    /// <param name="query">The keyword query; see the parameter description for the supported syntax.</param>
    /// <param name="maxResults">Maximum number of conversations to return (1–10). Pass 0 to use the default from config.</param>
    [Description("Keyword (full-text) search over the user's past conversations. Matches literal words, not meaning.")]
    public async Task<string> SearchMemoriesKeywordAsync(
        [Description("The words that must appear in the conversation. Use a few distinctive keywords rather than a sentence. " +
            "Double quotes require an exact phrase (\"native library interop\"); a leading - excludes a word (-android). " +
            "Matching is case-insensitive and lightly stemmed; it is otherwise literal, so spelling matters.")] string query,
        [Description("Maximum number of conversations to return (1-10). Defaults to 5 if omitted or 0.")] int maxResults = 0)
    {
        var limit = Math.Clamp(maxResults > 0 ? maxResults : _options.ToolMaxResults, 1, 10);

        logger.LogInformation(
            "search_memories_keyword tool invoked. Query: {Query}, MaxResults: {MaxResults}",
            query, limit);

        OnStarted?.Invoke(ToolName);

        try
        {
            var results = await repository.SearchTextAsync(query, limit, currentUser.UserId);

            logger.LogInformation(
                "search_memories_keyword: {Count} matching conversation(s) for query {Query}.",
                results.Count, query);

            if (results.Count == 0)
            {
                LastSources = [];
                return "No past conversations contain those exact words. "
                    + "Try fewer or different keywords, or use search_memories to search by meaning instead.";
            }

            LastSources = BuildSources(results);

            return FormatResults(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "search_memories_keyword tool failed.");
            LastSources = [];
            return $"Keyword search failed: {ex.Message}. Responding without memory context.";
        }
        finally
        {
            OnCompleted?.Invoke(ToolName);
        }
    }

    /// <summary>
    /// Renders search hits as text for the LLM, preferring the matched snippets and falling
    /// back to the summary when the snippet builder found nothing (a stemmed database match
    /// need not be a literal substring).
    /// </summary>
    private static string FormatResults(IReadOnlyList<ConversationTextSearchResult> results)
    {
        var result = new StringBuilder();
        result.AppendLine($"Found {results.Count} past conversation(s) containing the search terms, best match first:");
        result.AppendLine();

        foreach (var r in results)
        {
            result.AppendLine($"--- {r.Title ?? "Untitled"} ---");

            if (!string.IsNullOrWhiteSpace(r.Summary))
                result.AppendLine($"Summary: {r.Summary}");

            if (r.Snippets.Count > 0)
            {
                result.AppendLine("Matching excerpts:");
                foreach (var snippet in r.Snippets)
                    result.AppendLine($"  {snippet}");
            }

            result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts hits into <see cref="ChatSource"/> records for the response metadata.
    /// </summary>
    /// <remarks>
    /// Backend text ranks (MongoDB textScore, Postgres ts_rank_cd) are on arbitrary scales
    /// that would look broken next to the 0–1 cosine similarities from vector search, and
    /// the two sets get merged into one source list. Scores are therefore rescaled to a
    /// relative 0–1 rank within this result set: the ordering is faithful, the absolute
    /// value only means "how strong compared to the best keyword hit here".
    /// </remarks>
    private static List<ChatSource> BuildSources(IReadOnlyList<ConversationTextSearchResult> results)
    {
        var maxScore = ConversationTextSearchRanking.MaxScore(results);

        return [.. results.Select(r => new ChatSource(
            r.ConversationId,
            r.Title,
            r.Summary,
            ConversationTextSearchRanking.Relative(r.Score, maxScore)))];
    }
}
