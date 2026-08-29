using System.ComponentModel;
using System.Text;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides the <c>search_memories</c> tool function that the LLM can invoke
/// to search past conversation history. This enables tool-calling RAG where
/// the LLM decides when and how to search rather than always injecting context.
/// </summary>
/// <remarks>
/// This is a semantic (vector similarity) search, not a keyword or full-text search:
/// the query is embedded and compared against one embedding per stored conversation,
/// built from its title, summary, and message content. The tool and parameter
/// descriptions below are deliberately explicit about that, because models default to
/// writing keyword-style queries ("keycloak auth AND blazor error") which embed poorly;
/// a plain natural-language description of the subject retrieves far better.
/// Keyword-shaped queries belong in <see cref="KeywordSearchMemoriesTool"/>, which both this
/// description and its own point the model towards - keep the two descriptions in sync.
/// </remarks>
public class SearchMemoriesTool(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    IConversationRepository repository,
    IOptions<RagOptions> options,
    ICurrentUserService currentUser,
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
            description: "Semantic (vector similarity) search over the user's past conversations. " +
                "The query is turned into an embedding and matched against whole conversations by MEANING, not by keyword. " +
                "Write the query as a short natural-language description of the subject, phrased the way it would be said in conversation " +
                "(e.g. \"setting up Keycloak authentication in the Blazor app\"), " +
                "NOT as keywords, boolean operators, quoted phrases, wildcards, or field filters - those retrieve worse, not better. " +
                "Paraphrases and synonyms match well; exact strings, rare identifiers, error codes, and dates do not match reliably - " +
                "use search_memories_keyword for those, it searches the same conversations by literal word. " +
                "ALWAYS call this tool when the user asks about past conversations, references something they may have discussed before, " +
                "mentions a project, person, topic, or event that could be in their history, " +
                "or when you are uncertain whether you have relevant context. " +
                "If nothing useful comes back, retry once with the same topic described differently or more broadly - " +
                "do not retry with keywords here, switch to search_memories_keyword instead. " +
                "The search results contain actual conversation excerpts you should use to answer the user's question. " +
                "After calling this tool, incorporate the returned information directly into your response.");
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
    /// Searches past conversation history by embedding the query and retrieving the
    /// nearest conversation embeddings from the vector store. Matching is semantic, so
    /// the query only needs to describe the subject - it does not need to share wording
    /// with the stored conversations. Returns formatted excerpts that the LLM can use to
    /// formulate its response.
    /// </summary>
    /// <param name="query">Natural-language description of the topic to find; embedded and matched by meaning.</param>
    /// <param name="maxResults">Maximum number of conversations to return (1–10). Pass 0 to use the default from config.</param>
    [Description("Semantic (vector similarity) search over the user's past conversations. Matches on meaning, not keywords.")]
    public async Task<string> SearchMemoriesAsync(
        [Description("Natural-language description of the topic to find, written as prose - a sentence or descriptive phrase. " +
            "It is embedded and compared to past conversations by meaning, so describe the subject rather than listing keywords. " +
            "Do not use boolean operators, quoted exact phrases, wildcards, or field filters; they only make the match worse.")] string query,
        [Description("Maximum number of conversations to return (1-10). Defaults to 5 if omitted or 0. " +
            "Results are ranked by similarity and weak matches are dropped, so a larger value may still return fewer.")] int maxResults = 0)
    {
        var limit = Math.Clamp(maxResults > 0 ? maxResults : _options.ToolMaxResults, 1, 10);

        logger.LogInformation(
            "search_memories tool invoked. Query: {Query}, MaxResults: {MaxResults}",
            query, limit);

        OnStarted?.Invoke("search_memories");

        try
        {
            // 1. Embed the query with the same model used to embed the stored conversations.
            var embeddings = await embeddingGenerator.GenerateAsync([query]);
            var queryVector = embeddings[0].Vector.ToArray();

            // 2. Nearest-neighbour search over conversation embeddings, ranked by similarity.
            var searchResults = await vectorStore.SearchAsync(queryVector, limit, currentUser.UserId);

            // 3. Apply minimum score threshold using MinScore (same threshold as WithPrompt mode).
            var relevant = searchResults
                .Where(r => r.Score >= _options.MinScore)
                .ToList();

            logger.LogInformation(
                "search_memories: {Total} results from vector store, {Relevant} above MinScore {MinScore:F2}.",
                searchResults.Count, relevant.Count, _options.MinScore);

            if (relevant.Count == 0)
            {
                LastSources = [];
                return "No past conversations were semantically similar enough to this query. "
                    + "If you expected a match, search again describing the same topic in different or broader terms "
                    + "rather than adding keywords.";
            }

            // 4. Fetch full conversations from MongoDB.
            var fullConversations = await repository.GetByIdsAsync(relevant.Select(r => r.ConversationId));
            var conversationLookup = fullConversations.ToDictionary(c => c.ConversationId);

            // 5. Build formatted results.
            var result = new StringBuilder();
            result.AppendLine($"Found {relevant.Count} past conversation(s) semantically similar to the query, most similar first:");
            result.AppendLine();

            foreach (var r in relevant)
            {
                result.AppendLine($"--- {r.Title ?? "Untitled"} (similarity: {r.Score:F2}) ---");

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
        finally
        {
            OnCompleted?.Invoke("search_memories");
        }
    }
}
