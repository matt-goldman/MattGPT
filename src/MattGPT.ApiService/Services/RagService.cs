using System.Runtime.CompilerServices;
using System.Text;
using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

// Alias to avoid collision with the Blazor-side ChatMessage record.
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

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
/// Supports three modes configured via <see cref="RagOptions.Mode"/>:
/// <list type="bullet">
/// <item><see cref="RagMode.WithPrompt"/> — full automatic RAG injection on every message (no tools).</item>
/// <item><see cref="RagMode.Auto"/> — light auto-RAG plus a <c>search_memories</c> tool for deeper retrieval.</item>
/// <item><see cref="RagMode.ToolsOnly"/> — no auto-RAG; the LLM must use the <c>search_memories</c> tool explicitly.</item>
/// </list>
/// </summary>
public class RagService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IQdrantService qdrantService,
    IConversationRepository repository,
    IChatClient chatClient,
    IOptions<RagOptions> options,
    IOptions<ChatSessionOptions> chatOptions,
    ILogger<RagService> logger,
    SearchMemoriesTool? searchMemoriesTool = null)
{
    /// <summary>
    /// Maximum number of message characters to include per conversation in the context window.
    /// Prevents a single long conversation from consuming the entire context budget.
    /// </summary>
    public const int MaxExcerptCharsPerConversation = 4_000;
    private readonly RagOptions _options = options.Value;
    private readonly ChatSessionOptions _chatOptions = chatOptions.Value;

    /// <summary>
    /// Returns the effective TopK for the current mode's automatic retrieval pass.
    /// Returns 0 for <see cref="RagMode.ToolsOnly"/> (no automatic retrieval).
    /// </summary>
    internal int EffectiveTopK => _options.Mode switch
    {
        RagMode.Auto => _options.AutoTopK,
        RagMode.ToolsOnly => 0,
        _ => _options.TopK,
    };

    /// <summary>
    /// Returns the effective MinScore for the current mode's automatic retrieval pass.
    /// </summary>
    internal float EffectiveMinScore => _options.Mode switch
    {
        RagMode.Auto => _options.AutoMinScore,
        _ => _options.MinScore,
    };

    /// <summary>
    /// Builds <see cref="ChatOptions"/> with tool definitions when the mode supports it.
    /// Returns null for <see cref="RagMode.WithPrompt"/> (no tools).
    /// </summary>
    internal ChatOptions? BuildToolChatOptions()
    {
        if (_options.Mode == RagMode.WithPrompt || searchMemoriesTool is null)
            return null;

        return new ChatOptions
        {
            Tools = [searchMemoriesTool.CreateAIFunction()],
            ToolMode = ChatToolMode.Auto,
        };
    }

    /// <summary>
    /// Runs the full RAG pipeline for a user query and returns the LLM answer with sources.
    /// </summary>
    public async Task<RagChatResponse> ChatAsync(string query, ChatSession? session = null, CancellationToken ct = default)
    {
        logger.LogInformation("RAG ChatAsync called. Query: {Query}, Mode: {Mode}", query, _options.Mode);

        // 1. Automatic retrieval (full, light, or none depending on mode).
        var (relevant, conversationLookup) = await AutoRetrieveAsync(query, ct);

        // 2. Build the augmented prompt.
        var messages = BuildMessages(query, relevant, conversationLookup, session, _chatOptions.RecentMessageCount);

        var systemLen = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text?.Length ?? 0;
        logger.LogDebug("Built prompt with {MessageCount} messages. System message: {SystemChars} chars.", messages.Count, systemLen);

        // 3. Call the LLM (with tools if mode supports it).
        var chatOptions = BuildToolChatOptions();
        var response = await chatClient.GetResponseAsync(messages, chatOptions, ct);

        logger.LogDebug("LLM response length: {ResponseLength} chars.", response.Text?.Length ?? 0);

        // 4. Merge sources from auto-retrieval and any tool invocations.
        var sources = CollectAllSources(relevant);

        return new RagChatResponse(response.Text ?? string.Empty, sources);
    }

    /// <summary>
    /// Streaming variant of <see cref="ChatAsync"/>. Yields text tokens as they arrive
    /// from the LLM, then a final chunk containing the sources used.
    /// </summary>
    public async IAsyncEnumerable<RagStreamChunk> ChatStreamAsync(
        string query,
        ChatSession? session = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogInformation("RAG ChatStreamAsync called. Query: {Query}, Mode: {Mode}", query, _options.Mode);

        // 1. Automatic retrieval (full, light, or none depending on mode).
        var (relevant, conversationLookup) = await AutoRetrieveAsync(query, ct);

        // 2. Build the augmented prompt.
        var messages = BuildMessages(query, relevant, conversationLookup, session, _chatOptions.RecentMessageCount);

        var systemLen = messages.FirstOrDefault(m => m.Role == ChatRole.System)?.Text?.Length ?? 0;
        logger.LogDebug("Built prompt with {MessageCount} messages. System message: {SystemChars} chars.", messages.Count, systemLen);

        // 3. Stream from the LLM (with tools if mode supports it).
        var chatOptions = BuildToolChatOptions();
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new RagStreamChunk(update.Text);
            }
        }

        // 4. Final chunk carries the merged sources.
        var sources = CollectAllSources(relevant);

        yield return new RagStreamChunk(null, sources);
    }

    /// <summary>
    /// Performs the automatic retrieval pass: embed → Qdrant search → MongoDB fetch.
    /// In <see cref="RagMode.ToolsOnly"/> mode, skips retrieval entirely.
    /// In <see cref="RagMode.Auto"/> mode, uses lighter parameters (fewer results, higher threshold).
    /// </summary>
    private async Task<(IReadOnlyList<QdrantSearchResult> Relevant, IReadOnlyDictionary<string, StoredConversation> ConversationLookup)> AutoRetrieveAsync(
        string query, CancellationToken ct)
    {
        var topK = EffectiveTopK;

        if (topK <= 0)
        {
            logger.LogDebug("Mode={Mode}: skipping automatic retrieval.", _options.Mode);
            return ([], new Dictionary<string, StoredConversation>());
        }

        // 1. Embed the query.
        var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();
        logger.LogDebug("Generated query embedding with {Dimensions} dimensions.", queryVector.Length);

        // 2. Retrieve top-K candidates from Qdrant.
        var searchResults = await qdrantService.SearchAsync(queryVector, topK, ct);

        if (searchResults.Count == 0)
        {
            logger.LogWarning(
                "Qdrant returned 0 results for query. Check that embeddings have been generated " +
                "(POST /conversations/embed) and that the Qdrant collection is populated.");
        }
        else
        {
            foreach (var r in searchResults)
            {
                logger.LogDebug(
                    "Qdrant result: ConversationId={ConversationId}, Score={Score:F4}, Title={Title}",
                    r.ConversationId, r.Score, r.Title);
            }
        }

        // 3. Apply minimum similarity threshold.
        var minScore = EffectiveMinScore;
        var relevant = searchResults
            .Where(r => r.Score >= minScore)
            .ToList();

        logger.LogInformation(
            "RAG auto-retrieval (Mode={Mode}): {Total} results from Qdrant; {Relevant} meet MinScore threshold of {Threshold:F2}.",
            _options.Mode, searchResults.Count, relevant.Count, minScore);

        if (searchResults.Count > 0 && relevant.Count == 0)
        {
            logger.LogWarning(
                "All {Total} Qdrant results were below MinScore={MinScore:F2}. " +
                "Highest score was {MaxScore:F4}. Consider lowering RAG:MinScore in configuration.",
                searchResults.Count, minScore, searchResults.Max(r => r.Score));
        }

        // 4. Fetch full conversations from MongoDB to enrich context.
        IReadOnlyDictionary<string, StoredConversation> conversationLookup;
        if (relevant.Count > 0)
        {
            var fullConversations = await repository.GetByIdsAsync(relevant.Select(r => r.ConversationId), ct);
            conversationLookup = fullConversations.ToDictionary(c => c.ConversationId);

            logger.LogInformation(
                "Fetched {Found}/{Requested} full conversations from MongoDB for context enrichment.",
                fullConversations.Count, relevant.Count);
        }
        else
        {
            conversationLookup = new Dictionary<string, StoredConversation>();
        }

        return (relevant, conversationLookup);
    }

    /// <summary>
    /// Merges sources from automatic retrieval with any sources from tool invocations.
    /// De-duplicates by conversation ID, preferring the higher score.
    /// </summary>
    private IReadOnlyList<ChatSource> CollectAllSources(IReadOnlyList<QdrantSearchResult> autoRetrieved)
    {
        var sourcesDict = new Dictionary<string, ChatSource>();

        // Auto-retrieved sources.
        foreach (var r in autoRetrieved)
        {
            sourcesDict[r.ConversationId] = new ChatSource(r.ConversationId, r.Title, r.Summary, r.Score);
        }

        // Tool-retrieved sources (may overlap with auto-retrieved).
        if (searchMemoriesTool is not null)
        {
            foreach (var s in searchMemoriesTool.LastSources)
            {
                if (!sourcesDict.TryGetValue(s.ConversationId, out var existing) || s.Score > existing.Score)
                {
                    sourcesDict[s.ConversationId] = s;
                }
            }
        }

        return [.. sourcesDict.Values.OrderByDescending(s => s.Score)];
    }

    /// <summary>
    /// Builds the chat message list with a system prompt that frames retrieved conversations
    /// as the assistant's own memory, includes full conversation excerpts from MongoDB,
    /// and inserts session context (rolling summary + recent messages) for multi-turn support.
    /// </summary>
    public static List<AIChatMessage> BuildMessages(
        string query,
        IReadOnlyList<QdrantSearchResult> context,
        IReadOnlyDictionary<string, StoredConversation>? fullConversations = null,
        ChatSession? session = null,
        int recentMessageCount = 6)
    {
        var messages = new List<AIChatMessage>();

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

        messages.Add(new AIChatMessage(ChatRole.System, system.ToString()));

        // --- Rolling summary (medium-term memory) ---
        if (session?.RollingSummary is { Length: > 0 } rollingSummary)
        {
            var summaryBlock = new StringBuilder();
            summaryBlock.AppendLine("=== CONVERSATION SO FAR ===");
            summaryBlock.AppendLine(rollingSummary);
            summaryBlock.AppendLine("=== END CONVERSATION SUMMARY ===");
            messages.Add(new AIChatMessage(ChatRole.System, summaryBlock.ToString()));
        }

        // --- Recent messages (short-term memory) ---
        // Messages already in the session are included as prior turns so the LLM
        // sees the conversation history. The current user query is already in
        // session.Messages at this point, but we exclude it from the history via
        // SkipLast(1) since we add it explicitly as the final user message below.
        if (session is { Messages.Count: > 0 })
        {
            // Only include RECENT messages verbatim — older messages are already
            // compressed into the rolling summary above.  We take the recent window
            // (recentMessageCount) plus the current query, then SkipLast(1) to
            // exclude the current query which is added as the final user message below.
            var recentWindow = recentMessageCount + 1; // +1 for current query
            var historyMessages = session.Messages
                .TakeLast(recentWindow)
                .SkipLast(1);
            foreach (var msg in historyMessages)
            {
                var role = msg.Role switch
                {
                    "user" => ChatRole.User,
                    "assistant" => ChatRole.Assistant,
                    _ => ChatRole.User,
                };
                messages.Add(new AIChatMessage(role, msg.Content));
            }
        }

        // --- User message ---
        messages.Add(new AIChatMessage(ChatRole.User, query));

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
