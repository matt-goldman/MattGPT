using System.Runtime.CompilerServices;
using System.Text;
using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>A single retrieved conversation reference included in a chat response.</summary>
public record ChatSource(string ConversationId, string? Title, string? Summary, float Score);

/// <summary>The result of a RAG-augmented chat request.</summary>
public record RagChatResponse(string Answer, IReadOnlyList<ChatSource> Sources);

/// <summary>A single streamed chunk from the RAG pipeline.</summary>
/// <param name="Text">Incremental text token (null for the final "sources" frame).</param>
/// <param name="Sources">Set only on the final chunk to deliver source metadata.</param>
public record RagStreamChunk(string? Text, IReadOnlyList<ChatSource>? Sources = null);

/// <summary>
/// Implements the retrieval-augmented generation (RAG) pipeline.
/// Given a user query, generates an embedding, retrieves the most relevant past
/// conversations from Qdrant, fetches full conversation content from MongoDB,
/// constructs an augmented LLM prompt with system/user roles, and returns
/// the LLM's answer together with the sources used.
/// </summary>
public class RagService
{
    /// <summary>
    /// Maximum number of message characters to include per conversation in the context window.
    /// Prevents a single long conversation from consuming the entire context budget.
    /// </summary>
    public const int MaxExcerptCharsPerConversation = 4_000;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IQdrantService _qdrantService;
    private readonly IConversationRepository _repository;
    private readonly IChatClient _chatClient;
    private readonly RagOptions _options;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IQdrantService qdrantService,
        IConversationRepository repository,
        IChatClient chatClient,
        IOptions<RagOptions> options,
        ILogger<RagService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _qdrantService = qdrantService;
        _repository = repository;
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full RAG pipeline for a user query and returns the LLM answer with sources.
    /// </summary>
    public async Task<RagChatResponse> ChatAsync(string query, CancellationToken ct = default)
    {
        _logger.LogInformation("RAG ChatAsync called. Query: {Query}", query);

        // 1. Embed the query.
        var embeddings = await _embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        _logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // 2. Retrieve top-K candidates from Qdrant.
        var searchResults = await _qdrantService.SearchAsync(queryVector, _options.TopK, ct);

        if (searchResults.Count == 0)
        {
            _logger.LogWarning(
                "Qdrant returned 0 results for query. Check that embeddings have been generated " +
                "(POST /conversations/embed) and that the Qdrant collection is populated.");
        }
        else
        {
            foreach (var r in searchResults)
            {
                _logger.LogDebug(
                    "Qdrant result: ConversationId={ConversationId}, Score={Score:F4}, Title={Title}",
                    r.ConversationId, r.Score, r.Title);
            }
        }

        // 3. Apply minimum similarity threshold.
        var relevant = searchResults
            .Where(r => r.Score >= _options.MinScore)
            .ToList();

        _logger.LogInformation(
            "RAG query retrieved {Total} results from Qdrant; {Relevant} meet MinScore threshold of {Threshold:F2}.",
            searchResults.Count, relevant.Count, _options.MinScore);

        if (searchResults.Count > 0 && relevant.Count == 0)
        {
            _logger.LogWarning(
                "All {Total} Qdrant results were below MinScore={MinScore:F2}. " +
                "Highest score was {MaxScore:F4}. Consider lowering RAG:MinScore in configuration.",
                searchResults.Count, _options.MinScore, searchResults.Max(r => r.Score));
        }

        // 4. Fetch full conversations from MongoDB to enrich context.
        var fullConversations = relevant.Count > 0
            ? await _repository.GetByIdsAsync(relevant.Select(r => r.ConversationId), ct)
            : [];
        var conversationLookup = fullConversations.ToDictionary(c => c.ConversationId);

        _logger.LogInformation(
            "Fetched {Found}/{Requested} full conversations from MongoDB for context enrichment.",
            fullConversations.Count, relevant.Count);

        // 5. Build the augmented prompt using proper chat message roles.
        var messages = BuildMessages(query, relevant, conversationLookup);

        var systemLen = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text?.Length ?? 0;
        _logger.LogDebug("Built prompt with {MessageCount} messages. System message: {SystemChars} chars.", messages.Count, systemLen);

        // 6. Call the LLM.
        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);

        _logger.LogDebug("LLM response length: {ResponseLength} chars.", response.Text?.Length ?? 0);

        var sources = relevant
            .Select(r => new ChatSource(r.ConversationId, r.Title, r.Summary, r.Score))
            .ToList();

        return new RagChatResponse(response.Text ?? string.Empty, sources);
    }

    /// <summary>
    /// Streaming variant of <see cref="ChatAsync"/>. Yields text tokens as they arrive
    /// from the LLM, then a final chunk containing the sources used.
    /// </summary>
    public async IAsyncEnumerable<RagStreamChunk> ChatStreamAsync(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("RAG ChatStreamAsync called. Query: {Query}", query);

        // 1. Embed the query.
        var embeddings = await _embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        _logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // 2. Retrieve top-K candidates from Qdrant.
        var searchResults = await _qdrantService.SearchAsync(queryVector, _options.TopK, ct);

        if (searchResults.Count == 0)
        {
            _logger.LogWarning(
                "Qdrant returned 0 results for streaming query. Check that embeddings have been generated " +
                "(POST /conversations/embed) and that the Qdrant collection is populated.");
        }
        else
        {
            foreach (var r in searchResults)
            {
                _logger.LogDebug(
                    "Qdrant result: ConversationId={ConversationId}, Score={Score:F4}, Title={Title}",
                    r.ConversationId, r.Score, r.Title);
            }
        }

        // 3. Apply minimum similarity threshold.
        var relevant = searchResults
            .Where(r => r.Score >= _options.MinScore)
            .ToList();

        _logger.LogInformation(
            "RAG streaming query retrieved {Total} results from Qdrant; {Relevant} meet MinScore threshold of {Threshold:F2}.",
            searchResults.Count, relevant.Count, _options.MinScore);

        if (searchResults.Count > 0 && relevant.Count == 0)
        {
            _logger.LogWarning(
                "All {Total} Qdrant results were below MinScore={MinScore:F2}. " +
                "Highest score was {MaxScore:F4}. Consider lowering RAG:MinScore in configuration.",
                searchResults.Count, _options.MinScore, searchResults.Max(r => r.Score));
        }

        // 4. Fetch full conversations from MongoDB to enrich context.
        var fullConversations = relevant.Count > 0
            ? await _repository.GetByIdsAsync(relevant.Select(r => r.ConversationId), ct)
            : [];
        var conversationLookup = fullConversations.ToDictionary(c => c.ConversationId);

        _logger.LogInformation(
            "Fetched {Found}/{Requested} full conversations from MongoDB for context enrichment.",
            fullConversations.Count, relevant.Count);

        // 5. Build the augmented prompt using proper chat message roles.
        var messages = BuildMessages(query, relevant, conversationLookup);

        var systemLen = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text?.Length ?? 0;
        _logger.LogDebug("Built prompt with {MessageCount} messages. System message: {SystemChars} chars.", messages.Count, systemLen);

        // 6. Stream from the LLM.
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new RagStreamChunk(update.Text);
            }
        }

        // 7. Final chunk carries the sources.
        var sources = relevant
            .Select(r => new ChatSource(r.ConversationId, r.Title, r.Summary, r.Score))
            .ToList();

        yield return new RagStreamChunk(null, sources);
    }

    /// <summary>
    /// Builds the chat message list with a system prompt that frames retrieved conversations
    /// as the assistant's own memory, and includes full conversation excerpts from MongoDB.
    /// </summary>
    public static List<ChatMessage> BuildMessages(
        string query,
        IReadOnlyList<QdrantSearchResult> context,
        IReadOnlyDictionary<string, StoredConversation>? fullConversations = null)
    {
        var messages = new List<ChatMessage>();

        // --- System message: identity + memory framing ---
        var system = new StringBuilder();
        system.AppendLine("""
            You are a knowledgeable personal assistant. You have access to the user's complete history of past conversations.
            When memories are provided below, treat them as your own recollections of past interactions with the user — not as external documents.
            Draw on these memories naturally to give informed, contextual answers. Reference specific details, decisions, or preferences the user expressed in those conversations.
            If the memories contain code, technical decisions, or project context, use that knowledge as if you were the assistant in those original conversations.
            If no relevant memories are found, answer from general knowledge but let the user know you don't have any relevant memories on that topic.
            """);

        if (context.Count > 0)
        {
            system.AppendLine();
            system.AppendLine("=== YOUR MEMORIES ===");
            system.AppendLine();

            for (int i = 0; i < context.Count; i++)
            {
                var c = context[i];
                system.AppendLine($"--- Memory {i + 1} ---");

                if (!string.IsNullOrWhiteSpace(c.Title))
                    system.AppendLine($"Topic: {c.Title}");

                if (!string.IsNullOrWhiteSpace(c.Summary))
                    system.AppendLine($"Summary: {c.Summary}");

                // Include full conversation excerpt from MongoDB if available.
                if (fullConversations is not null
                    && fullConversations.TryGetValue(c.ConversationId, out var full)
                    && full.LinearisedMessages.Count > 0)
                {
                    system.AppendLine("Conversation excerpt:");
                    var excerpt = BuildConversationExcerpt(full);
                    system.AppendLine(excerpt);
                }

                system.AppendLine();
            }

            system.AppendLine("=== END MEMORIES ===");
        }
        else
        {
            system.AppendLine();
            system.AppendLine("No relevant memories were found for this query.");
        }

        messages.Add(new ChatMessage(ChatRole.System, system.ToString()));

        // --- User message ---
        messages.Add(new ChatMessage(ChatRole.User, query));

        return messages;
    }

    /// <summary>
    /// Builds a truncated human-readable excerpt of a conversation's messages,
    /// limited to <see cref="MaxExcerptCharsPerConversation"/> characters.
    /// </summary>
    public static string BuildConversationExcerpt(StoredConversation conversation)
    {
        var sb = new StringBuilder();
        foreach (var msg in conversation.LinearisedMessages)
        {
            var role = msg.Role switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                "tool" => "Tool",
                _ => msg.Role,
            };

            var content = string.Join(" ", msg.Parts);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var line = $"{role}: {content}";

            if (sb.Length + line.Length + 1 > MaxExcerptCharsPerConversation)
            {
                // Truncate and signal there's more.
                var remaining = MaxExcerptCharsPerConversation - sb.Length - 20;
                if (remaining > 0)
                    sb.AppendLine(line[..remaining] + "...");
                sb.AppendLine("[conversation truncated]");
                break;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }
}
