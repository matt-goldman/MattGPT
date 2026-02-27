using System.ComponentModel;
using System.Text;
using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides the <c>search_memories</c> tool function that the LLM can invoke
/// to search past conversation history. This enables tool-calling RAG where
/// the LLM decides when and how to search rather than always injecting context.
/// </summary>
public class SearchMemoriesTool(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IQdrantService qdrantService,
    IConversationRepository repository,
    IOptions<RagOptions> options,
    ILogger<SearchMemoriesTool> logger)
{
    private readonly RagOptions _options = options.Value;

    /// <summary>
    /// Sources retrieved by the most recent tool invocation. Populated after
    /// <see cref="SearchMemoriesAsync"/> is called by the LLM tool-call loop.
    /// </summary>
    public IReadOnlyList<ChatSource> LastSources { get; private set; } = [];

    /// <summary>
    /// Creates an <see cref="AIFunction"/> wrapping <see cref="SearchMemoriesAsync"/>
    /// that can be passed to <see cref="ChatOptions.Tools"/>.
    /// </summary>
    public AIFunction CreateAIFunction()
    {
        return AIFunctionFactory.Create(
            SearchMemoriesAsync,
            name: "search_memories",
            description: "Search the user's past conversation history by topic or query. " +
                "Use this when the user asks about something you discussed before, references a past project, " +
                "or when additional context from prior conversations would help you give a better answer.");
    }

    /// <summary>
    /// Searches past conversation history by embedding the query and retrieving
    /// similar conversations from the vector store. Returns formatted excerpts
    /// that the LLM can use to formulate its response.
    /// </summary>
    /// <param name="query">The search query describing what to look for in past conversations.</param>
    /// <param name="maxResults">Maximum number of conversations to return (1–10, default from config).</param>
    [Description("Search the user's past conversation history by topic or query.")]
    public async Task<string> SearchMemoriesAsync(
        [Description("The search query describing what to look for in past conversations.")] string query,
        [Description("Maximum number of conversations to return (1-10).")] int? maxResults = null)
    {
        var limit = Math.Clamp(maxResults ?? _options.ToolMaxResults, 1, 10);

        logger.LogInformation(
            "search_memories tool invoked. Query: {Query}, MaxResults: {MaxResults}",
            query, limit);

        try
        {
            // 1. Embed the tool query.
            var embeddings = await embeddingGenerator.GenerateAsync([query]);
            var queryVector = embeddings[0].Vector.ToArray();

            // 2. Search Qdrant.
            var searchResults = await qdrantService.SearchAsync(queryVector, limit);

            // 3. Apply minimum score threshold (use auto-mode threshold for tool results).
            var relevant = searchResults
                .Where(r => r.Score >= _options.MinScore)
                .ToList();

            logger.LogInformation(
                "search_memories: {Total} results from Qdrant, {Relevant} above MinScore {MinScore:F2}.",
                searchResults.Count, relevant.Count, _options.MinScore);

            if (relevant.Count == 0)
            {
                LastSources = [];
                return "No relevant past conversations found for this query.";
            }

            // 4. Fetch full conversations from MongoDB.
            var fullConversations = await repository.GetByIdsAsync(relevant.Select(r => r.ConversationId));
            var conversationLookup = fullConversations.ToDictionary(c => c.ConversationId);

            // 5. Build formatted results.
            var result = new StringBuilder();
            result.AppendLine($"Found {relevant.Count} relevant past conversation(s):");
            result.AppendLine();

            foreach (var r in relevant)
            {
                result.AppendLine($"--- {r.Title ?? "Untitled"} (relevance: {r.Score:F2}) ---");

                if (!string.IsNullOrWhiteSpace(r.Summary))
                    result.AppendLine($"Summary: {r.Summary}");

                if (conversationLookup.TryGetValue(r.ConversationId, out var full)
                    && full.LinearisedMessages.Count > 0)
                {
                    result.AppendLine("Excerpt:");
                    result.AppendLine(RagService.BuildConversationExcerpt(full));
                }

                result.AppendLine();
            }

            // Track sources for the response metadata.
            LastSources = relevant
                .Select(r => new ChatSource(r.ConversationId, r.Title, r.Summary, r.Score))
                .ToList();

            return result.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "search_memories tool failed.");
            LastSources = [];
            return $"Memory search failed: {ex.Message}. Responding without memory context.";
        }
    }
}
