using MattGPT.ApiService;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IVectorStore that returns configured search results.
/// </summary>
internal sealed class FakeSearchVectorStore(IReadOnlyList<VectorSearchResult> results) : IVectorStore
{
    public Task UpsertAsync(MattGPT.ApiService.Models.StoredConversation conversation, float[] vector, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        => Task.FromResult(results);

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>((ulong)results.Count);
}

public class RagServiceTests
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    private static StoredConversation MakeConversation(string id, string title, string? summary = null, int messageCount = 2)
    {
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = i % 2 == 0 ? "user" : "assistant",
                ContentType = "text",
                Parts = [$"Message content {i} from {title}"],
            })
            .ToList();

        return new StoredConversation
        {
            ConversationId = id,
            Title = title,
            Summary = summary,
            LinearisedMessages = messages,
            ProcessingStatus = ConversationProcessingStatus.Embedded,
        };
    }

    private static RagService CreateService(
        IReadOnlyList<VectorSearchResult> searchResults,
        string llmResponse = "Test answer.",
        RagOptions? ragOptions = null,
        FakeConversationRepository? repository = null,
        ChatSessionOptions? chatOptions = null,
        SearchMemoriesTool? searchMemoriesTool = null)
    {
        var options = Options.Create(ragOptions ?? new RagOptions { TopK = 5, MinScore = 0.5f });
        var chatOpts = Options.Create(chatOptions ?? new ChatSessionOptions());
        return new RagService(
            new FakeEmbeddingGenerator(TestVector),
            new FakeSearchVectorStore(searchResults),
            repository ?? new FakeConversationRepository(),
            new FakeChatClient(llmResponse),
            options,
            chatOpts,
            NullLogger<RagService>.Instance,
            new NullCurrentUserService(),
            searchMemoriesTool);
    }

    [Fact]
    public async Task ChatAsync_ReturnsLlmAnswer()
    {
        var service = CreateService([], llmResponse: "Hello from LLM.");

        var result = await service.ChatAsync("What did we discuss?");

        Assert.Equal("Hello from LLM.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_NoResults_ReturnsEmptySources()
    {
        var service = CreateService([]);

        var result = await service.ChatAsync("Any question");

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task ChatAsync_ResultsAboveThreshold_IncludedInSources()
    {
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.7f, "Title 2", "Summary 2"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Title 1", "Summary 1"), MakeConversation("c2", "Title 2", "Summary 2")]);
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f }, repository: repo);

        var result = await service.ChatAsync("query");

        Assert.Equal(2, result.Sources.Count);
        Assert.Contains(result.Sources, s => s.ConversationId == "c1");
        Assert.Contains(result.Sources, s => s.ConversationId == "c2");
    }

    [Fact]
    public async Task ChatAsync_ResultsBelowThreshold_ExcludedFromSources()
    {
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.3f, "Title 2", "Summary 2"), // below 0.5 threshold
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Title 1", "Summary 1")]);
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f }, repository: repo);

        var result = await service.ChatAsync("query");

        Assert.Single(result.Sources);
        Assert.Equal("c1", result.Sources[0].ConversationId);
    }

    [Fact]
    public async Task ChatAsync_AllResultsBelowThreshold_ReturnsEmptySources()
    {
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.2f, "Title 1", "Summary 1"),
            new("c2", 0.1f, "Title 2", "Summary 2"),
        };
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f });

        var result = await service.ChatAsync("query");

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task ChatAsync_SourcesContainCorrectMetadata()
    {
        var results = new List<VectorSearchResult>
        {
            new("conv-123", 0.85f, "My Title", "My Summary"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("conv-123", "My Title", "My Summary")]);
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f }, repository: repo);

        var result = await service.ChatAsync("query");

        var source = Assert.Single(result.Sources);
        Assert.Equal("conv-123", source.ConversationId);
        Assert.Equal("My Title", source.Title);
        Assert.Equal("My Summary", source.Summary);
        Assert.Equal(0.85f, source.Score);
    }

    [Fact]
    public void BuildMessages_WithContext_IncludesRetrievedConversations()
    {
        var context = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Test Title", "Test Summary"),
        };
        var fullConversations = new Dictionary<string, StoredConversation>
        {
            ["c1"] = MakeConversation("c1", "Test Title", "Test Summary"),
        };

        var messages = RagService.BuildMessages("What is this?", context, fullConversations);

        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);

        var systemText = messages[0].Text!;
        Assert.Contains("Test Title", systemText);
        Assert.Contains("Test Summary", systemText);
        Assert.Contains("YOUR MEMORIES", systemText);
        Assert.Contains("Message content", systemText); // Full conversation content
        Assert.Equal("What is this?", messages[1].Text);
    }

    [Fact]
    public void BuildMessages_WithoutContext_ContainsNoMemoriesMessage()
    {
        var messages = RagService.BuildMessages("Any query", []);

        Assert.Equal(2, messages.Count);
        Assert.Contains("No relevant memories", messages[0].Text!);
        Assert.Equal("Any query", messages[1].Text);
    }

    [Fact]
    public void BuildMessages_MultipleContextItems_AllIncluded()
    {
        var context = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title A", "Summary A"),
            new("c2", 0.8f, "Title B", "Summary B"),
            new("c3", 0.7f, "Title C", "Summary C"),
        };

        var messages = RagService.BuildMessages("query", context);

        var systemText = messages[0].Text!;
        Assert.Contains("Title A", systemText);
        Assert.Contains("Title B", systemText);
        Assert.Contains("Title C", systemText);
        Assert.Contains("Summary A", systemText);
        Assert.Contains("Summary B", systemText);
        Assert.Contains("Summary C", systemText);
    }

    [Fact]
    public void BuildMessages_SystemPromptFramesContextAsMemory()
    {
        var context = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title", "Summary"),
        };

        var messages = RagService.BuildMessages("query", context);

        var systemText = messages[0].Text!;
        Assert.Contains("recollections", systemText);
        Assert.Contains("memories", systemText, StringComparison.OrdinalIgnoreCase);
        // The prompt should frame context as the assistant's own memories,
        // not as external data to be consulted.
        Assert.DoesNotContain("retrieved to help answer", systemText);
    }

    [Fact]
    public void BuildMessages_IncludesFullConversationExcerpt()
    {
        var context = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Coding Help", "Helped with Python"),
        };
        var fullConversations = new Dictionary<string, StoredConversation>
        {
            ["c1"] = MakeConversation("c1", "Coding Help", "Helped with Python", messageCount: 4),
        };

        var messages = RagService.BuildMessages("query", context, fullConversations);

        var systemText = messages[0].Text!;
        Assert.Contains("Conversation excerpt:", systemText);
        Assert.Contains("User:", systemText);
        Assert.Contains("Assistant:", systemText);
    }

    [Fact]
    public void BuildConversationExcerpt_TruncatesLongConversations()
    {
        var conv = new StoredConversation
        {
            ConversationId = "long",
            Title = "Long conv",
            LinearisedMessages = [.. Enumerable.Range(0, 200)
                .Select(i => new StoredMessage
                {
                    Id = $"m{i}",
                    Role = i % 2 == 0 ? "user" : "assistant",
                    ContentType = "text",
                    Parts = [$"This is a moderately long message number {i} that should eventually cause truncation when enough messages accumulate."],
                })],
        };

        var excerpt = RagService.BuildConversationExcerpt(conv);

        Assert.True(excerpt.Length <= RagService.MaxExcerptCharsPerConversation + 100); // small buffer for final line
        Assert.Contains("[conversation truncated]", excerpt);
    }

    // --- Multi-turn / session-aware BuildMessages tests ---

    [Fact]
    public void BuildMessages_WithSession_IncludesRecentMessagesBeforeUserQuery()
    {
        var session = new ChatSession();
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "First question" });
        session.Messages.Add(new ChatSessionMessage { Role = "assistant", Content = "First answer" });
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "Follow-up question" });

        var messages = RagService.BuildMessages("Follow-up question", [], session: session);

        // Should be: System, User("First question"), Assistant("First answer"), User("Follow-up question")
        Assert.Equal(4, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Equal("First question", messages[1].Text);
        Assert.Equal(ChatRole.Assistant, messages[2].Role);
        Assert.Equal("First answer", messages[2].Text);
        Assert.Equal(ChatRole.User, messages[3].Role);
        Assert.Equal("Follow-up question", messages[3].Text);
    }

    [Fact]
    public void BuildMessages_WithRollingSummary_IncludesSummaryBlock()
    {
        var session = new ChatSession
        {
            RollingSummary = "User asked about Qdrant integration. Decisions were made about vector sizes.",
        };
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "Current question" });

        var messages = RagService.BuildMessages("Current question", [], session: session);

        // System + Summary system message + User
        Assert.True(messages.Count >= 3);
        var summaryMsg = messages[1];
        Assert.Equal(ChatRole.System, summaryMsg.Role);
        Assert.Contains("CONVERSATION SO FAR", summaryMsg.Text!);
        Assert.Contains("Qdrant integration", summaryMsg.Text!);
    }

    [Fact]
    public void BuildMessages_WithSessionAndRag_CombinesAllTiers()
    {
        var context = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "RAG Title", "RAG Summary"),
        };

        var session = new ChatSession
        {
            RollingSummary = "Prior conversation summary.",
        };
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "Earlier question" });
        session.Messages.Add(new ChatSessionMessage { Role = "assistant", Content = "Earlier answer" });
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "New question" });

        var messages = RagService.BuildMessages("New question", context, session: session);

        // System (with RAG) + System (summary) + User + Assistant + User (current)
        var systemTexts = messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text!).ToList();
        Assert.Contains(systemTexts, t => t.Contains("YOUR MEMORIES"));
        Assert.Contains(systemTexts, t => t.Contains("CONVERSATION SO FAR"));

        var userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
        Assert.Equal(2, userMessages.Count);
        Assert.Equal("Earlier question", userMessages[0].Text);
        Assert.Equal("New question", userMessages[1].Text);
    }

    [Fact]
    public void BuildMessages_NullSession_BehavesAsBefore()
    {
        var messages = RagService.BuildMessages("Simple query", []);

        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Equal("Simple query", messages[1].Text);
    }

    [Fact]
    public void BuildMessages_SingleMessageSession_OnlyIncludesUserQuery()
    {
        // When there's only one message (the current user query), there should be
        // no prior messages — just system + user.
        var session = new ChatSession();
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "Just one message" });

        var messages = RagService.BuildMessages("Just one message", [], session: session);

        Assert.Equal(2, messages.Count); // System + User
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Equal("Just one message", messages[1].Text);
    }

    [Fact]
    public void BuildMessages_WithManyMessages_OnlyIncludesRecentWindow()
    {
        // 10 exchanges (20 messages) + current query = 21 messages total.
        // With recentMessageCount=2, only the last 2 prior messages should appear verbatim.
        var session = new ChatSession();
        for (int i = 0; i < 10; i++)
        {
            session.Messages.Add(new ChatSessionMessage { Role = "user", Content = $"User msg {i}" });
            session.Messages.Add(new ChatSessionMessage { Role = "assistant", Content = $"Asst msg {i}" });
        }
        session.Messages.Add(new ChatSessionMessage { Role = "user", Content = "Current query" });

        var messages = RagService.BuildMessages("Current query", [], session: session, recentMessageCount: 2);

        // System + 2 recent prior messages + final user = 4
        Assert.Equal(4, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Equal("User msg 9", messages[1].Text);
        Assert.Equal(ChatRole.Assistant, messages[2].Role);
        Assert.Equal("Asst msg 9", messages[2].Text);
        Assert.Equal(ChatRole.User, messages[3].Role);
        Assert.Equal("Current query", messages[3].Text);

        // Older messages (e.g. "User msg 0") should NOT appear
        Assert.DoesNotContain(messages, m => m.Text == "User msg 0");
        Assert.DoesNotContain(messages, m => m.Text == "Asst msg 0");
    }

    // --- Mode-specific tests ---

    [Fact]
    public void EffectiveTopK_AutoMode_ReturnsFullTopK()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.WithPrompt, TopK = 5 });
        Assert.Equal(5, service.EffectiveTopK);
    }

    [Fact]
    public void EffectiveTopK_HybridMode_ReturnsHybridTopK()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.Auto, AutoTopK = 2 });
        Assert.Equal(2, service.EffectiveTopK);
    }

    [Fact]
    public void EffectiveTopK_ToolsMode_ReturnsZero()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.ToolsOnly });
        Assert.Equal(0, service.EffectiveTopK);
    }

    [Fact]
    public void EffectiveMinScore_AutoMode_ReturnsStandardMinScore()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.WithPrompt, MinScore = 0.5f });
        Assert.Equal(0.5f, service.EffectiveMinScore);
    }

    [Fact]
    public void EffectiveMinScore_HybridMode_ReturnsHybridMinScore()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.Auto, AutoMinScore = 0.65f });
        Assert.Equal(0.65f, service.EffectiveMinScore);
    }

    [Fact]
    public void BuildToolChatOptions_AutoMode_ReturnsNull()
    {
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.WithPrompt });
        Assert.Null(service.BuildToolChatOptions());
    }

    [Fact]
    public void BuildToolChatOptions_HybridMode_WithoutTool_ReturnsNull()
    {
        // No SearchMemoriesTool injected — should return null even in Auto mode.
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.Auto });
        Assert.Null(service.BuildToolChatOptions());
    }

    [Fact]
    public void BuildToolChatOptions_HybridMode_WithTool_ReturnsChatOptions()
    {
        var tool = CreateSearchMemoriesTool();
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.Auto }, searchMemoriesTool: tool);

        var chatOptions = service.BuildToolChatOptions();

        Assert.NotNull(chatOptions);
        Assert.Single(chatOptions!.Tools!);
        Assert.Equal(ChatToolMode.Auto, chatOptions.ToolMode);
    }

    [Fact]
    public void BuildToolChatOptions_ToolsMode_WithTool_ReturnsChatOptions()
    {
        var tool = CreateSearchMemoriesTool();
        var service = CreateService([], ragOptions: new RagOptions { Mode = RagMode.ToolsOnly }, searchMemoriesTool: tool);

        var chatOptions = service.BuildToolChatOptions();

        Assert.NotNull(chatOptions);
        Assert.Single(chatOptions!.Tools!);
    }

    [Fact]
    public async Task ChatAsync_ToolsMode_SkipsAutoRetrieval()
    {
        // In ToolsOnly mode, even if Qdrant has results, auto-retrieval should be skipped
        // (EffectiveTopK = 0), so sources should be empty (no tool was actually called
        // since our FakeChatClient doesn't call tools).
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
        };
        var service = CreateService(results, ragOptions: new RagOptions { Mode = RagMode.ToolsOnly });

        var result = await service.ChatAsync("query");

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task ChatAsync_HybridMode_UsesLighterParameters()
    {
        // Auto mode uses AutoTopK=2 and AutoMinScore=0.65.
        // A result with score 0.6 should be excluded (below 0.65 threshold).
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.6f, "Title 2", "Summary 2"), // below auto threshold of 0.65
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Title 1", "Summary 1"), MakeConversation("c2", "Title 2", "Summary 2")]);

        var service = CreateService(results, ragOptions: new RagOptions
        {
            Mode = RagMode.Auto,
            AutoTopK = 2,
            AutoMinScore = 0.65f,
        }, repository: repo);

        var result = await service.ChatAsync("query");

        Assert.Single(result.Sources);
        Assert.Equal("c1", result.Sources[0].ConversationId);
    }

    [Fact]
    public async Task ChatAsync_AutoMode_UsesFullParameters()
    {
        // WithPrompt mode uses TopK=5 and MinScore=0.5.
        // A result with score 0.6 should be included (above 0.5 threshold).
        var results = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.6f, "Title 2", "Summary 2"), // above WithPrompt threshold of 0.5
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Title 1", "Summary 1"), MakeConversation("c2", "Title 2", "Summary 2")]);

        var service = CreateService(results, ragOptions: new RagOptions
        {
            Mode = RagMode.WithPrompt,
            TopK = 5,
            MinScore = 0.5f,
        }, repository: repo);

        var result = await service.ChatAsync("query");

        Assert.Equal(2, result.Sources.Count);
    }

    private static SearchMemoriesTool CreateSearchMemoriesTool(
        IReadOnlyList<VectorSearchResult>? results = null,
        FakeConversationRepository? repository = null)
    {
        return new SearchMemoriesTool(
            new FakeEmbeddingGenerator(TestVector),
            new FakeSearchVectorStore(results ?? []),
            repository ?? new FakeConversationRepository(),
            Options.Create(new RagOptions()),
            new NullCurrentUserService(),
            NullLogger<SearchMemoriesTool>.Instance);
    }

    // ── Diagnostic mode tests ──

    [Fact]
    public async Task ChatAsync_DiagnosticMode_ExtractsResponseFromJson()
    {
        var jsonResponse = """{"reasoning":"I thought about it carefully.","response":"Here is my answer."}""";
        var service = CreateService([], llmResponse: jsonResponse,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("What did we discuss?");

        Assert.Equal("Here is my answer.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_FallsBackToRawOnParseFailure()
    {
        var rawResponse = "This is not JSON.";
        var service = CreateService([], llmResponse: rawResponse,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("query");

        Assert.Equal(rawResponse, result.Answer);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_StripsMdFencesBeforeParsing()
    {
        var fenced = "```json\n{\"reasoning\":\"R\",\"response\":\"Clean answer.\"}\n```";
        var service = CreateService([], llmResponse: fenced,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("query");

        Assert.Equal("Clean answer.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_ExtractsJsonPrefixedByProse()
    {
        var response = "Here is my response:\n{\"reasoning\":\"Thought about it.\",\"response\":\"The actual answer.\"}";
        var service = CreateService([], llmResponse: response,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("query");

        Assert.Equal("The actual answer.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_ExtractsJsonWrappedInProse()
    {
        var response = "Sure! Here you go:\n{\"reasoning\":\"Memory search.\",\"response\":\"Found it.\"}\nHope that helps!";
        var service = CreateService([], llmResponse: response,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("query");

        Assert.Equal("Found it.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_HandlesMarkdownContentGracefully()
    {
        // Simulate a model dumping retrieved markdown content instead of JSON.
        var markdownDump = "---\ntitle: \"Starting with Why\"\ndate: 2025-08-15\n---\n\n## Episode X\n\n**Summary**\n\nSome content here.";
        var service = CreateService([], llmResponse: markdownDump,
            ragOptions: new RagOptions { DiagnosticMode = true });

        var result = await service.ChatAsync("query");

        // Falls back to raw text since no JSON is present at all.
        Assert.Equal(markdownDump, result.Answer);
    }

    [Fact]
    public void EnumerateJsonCandidates_PlainJson_ReturnsSingleCandidate()
    {
        var json = "{\"reasoning\":\"R\",\"response\":\"A\"}";
        var candidates = RagService.EnumerateJsonCandidates(json).ToList();

        Assert.Single(candidates);
        Assert.Equal(json, candidates[0]);
    }

    [Fact]
    public void EnumerateJsonCandidates_JsonWithProse_ReturnsBraceExtracted()
    {
        var input = "Here is the result: {\"reasoning\":\"R\",\"response\":\"A\"} done.";
        var candidates = RagService.EnumerateJsonCandidates(input).ToList();

        Assert.Contains("{\"reasoning\":\"R\",\"response\":\"A\"}", candidates);
    }

    [Fact]
    public void EnumerateJsonCandidates_FencedJson_ReturnsFenceStripped()
    {
        var input = "```json\n{\"reasoning\":\"R\",\"response\":\"A\"}\n```";
        var candidates = RagService.EnumerateJsonCandidates(input).ToList();

        Assert.Contains("{\"reasoning\":\"R\",\"response\":\"A\"}", candidates);
    }

    [Fact]
    public void EnumerateJsonCandidates_NoJson_StillReturnsRawTrimmed()
    {
        var input = "No JSON here at all, just plain text.";
        var candidates = RagService.EnumerateJsonCandidates(input).ToList();

        // At minimum the raw trimmed text is returned as a candidate.
        Assert.Contains(input, candidates);
    }

    [Fact]
    public void BuildMessages_DiagnosticInstruction_ContainsBothFields()
    {
        Assert.Contains("\"reasoning\"", RagService.DiagnosticInstruction);
        Assert.Contains("\"response\"", RagService.DiagnosticInstruction);
    }

    [Fact]
    public async Task ChatAsync_DiagnosticMode_Off_ReturnsRawResponse()
    {
        var rawResponse = "Normal response without JSON.";
        var service = CreateService([], llmResponse: rawResponse,
            ragOptions: new RagOptions { DiagnosticMode = false });

        var result = await service.ChatAsync("query");

        Assert.Equal(rawResponse, result.Answer);
    }
}
