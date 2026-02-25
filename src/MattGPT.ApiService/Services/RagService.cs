using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>A single retrieved conversation reference included in a chat response.</summary>
public record ChatSource(string ConversationId, string? Title, string? Summary, float Score);

/// <summary>The result of a RAG-augmented chat request.</summary>
public record RagChatResponse(string Answer, IReadOnlyList<ChatSource> Sources);

/// <summary>
/// Implements the retrieval-augmented generation (RAG) pipeline.
/// Given a user query, generates an embedding, retrieves the most relevant past
/// conversations from Qdrant, constructs an augmented LLM prompt, and returns
/// the LLM's answer together with the sources used.
/// </summary>
public class RagService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IQdrantService _qdrantService;
    private readonly IChatClient _chatClient;
    private readonly RagOptions _options;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IQdrantService qdrantService,
        IChatClient chatClient,
        IOptions<RagOptions> options,
        ILogger<RagService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _qdrantService = qdrantService;
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full RAG pipeline for a user query and returns the LLM answer with sources.
    /// </summary>
    public async Task<RagChatResponse> ChatAsync(string query, CancellationToken ct = default)
    {
        // 1. Embed the query.
        var embeddings = await _embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();

        // 2. Retrieve top-K candidates from Qdrant.
        var searchResults = await _qdrantService.SearchAsync(queryVector, _options.TopK, ct);

        // 3. Apply minimum similarity threshold.
        var relevant = searchResults
            .Where(r => r.Score >= _options.MinScore)
            .ToList();

        _logger.LogInformation(
            "RAG query retrieved {Total} results; {Relevant} meet the minimum score threshold of {Threshold:F2}.",
            searchResults.Count, relevant.Count, _options.MinScore);

        // 4. Build the augmented prompt.
        var prompt = BuildPrompt(query, relevant);

        // 5. Call the LLM.
        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);

        var sources = relevant
            .Select(r => new ChatSource(r.ConversationId, r.Title, r.Summary, r.Score))
            .ToList();

        return new RagChatResponse(response.Text, sources);
    }

    /// <summary>
    /// Builds the RAG prompt that includes the retrieved context and the user query.
    /// </summary>
    public static string BuildPrompt(string query, IReadOnlyList<QdrantSearchResult> context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful assistant with access to a user's past ChatGPT conversation history.");
        sb.AppendLine();

        if (context.Count > 0)
        {
            sb.AppendLine("The following relevant past conversations have been retrieved to help answer the user's question:");
            sb.AppendLine();

            for (int i = 0; i < context.Count; i++)
            {
                var c = context[i];
                sb.Append($"[{i + 1}]");
                if (!string.IsNullOrWhiteSpace(c.Title))
                    sb.Append($" Title: {c.Title}.");
                if (!string.IsNullOrWhiteSpace(c.Summary))
                    sb.Append($" Summary: {c.Summary}");
                else
                    sb.Append(" (No summary available)");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("Use the above context to inform your answer where relevant. If the retrieved context does not help, answer from general knowledge.");
        }
        else
        {
            sb.AppendLine("No relevant past conversations were found. Answer from general knowledge.");
        }

        sb.AppendLine();
        sb.Append("User: ").AppendLine(query);
        sb.AppendLine();
        sb.Append("Assistant:");

        return sb.ToString();
    }
}
