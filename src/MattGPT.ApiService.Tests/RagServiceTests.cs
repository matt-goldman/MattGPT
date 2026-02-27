using MattGPT.ApiService;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IQdrantService that returns configured search results.
/// </summary>
internal sealed class FakeSearchQdrantService : IQdrantService
{
    private readonly IReadOnlyList<QdrantSearchResult> _results;

    public FakeSearchQdrantService(IReadOnlyList<QdrantSearchResult> results)
        => _results = results;

    public Task UpsertAsync(MattGPT.ApiService.Models.StoredConversation conversation, float[] vector, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, CancellationToken ct = default)
        => Task.FromResult(_results);

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>((ulong)_results.Count);
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
        IReadOnlyList<QdrantSearchResult> searchResults,
        string llmResponse = "Test answer.",
        RagOptions? ragOptions = null,
        FakeConversationRepository? repository = null,
        ChatSessionOptions? chatOptions = null)
    {
        var options = Options.Create(ragOptions ?? new RagOptions { TopK = 5, MinScore = 0.5f });
        var chatOpts = Options.Create(chatOptions ?? new ChatSessionOptions());
        return new RagService(
            new FakeEmbeddingGenerator(TestVector),
            new FakeSearchQdrantService(searchResults),
            repository ?? new FakeConversationRepository(),
            new FakeChatClient(llmResponse),
            options,
            chatOpts,
            NullLogger<RagService>.Instance);
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
        var results = new List<QdrantSearchResult>
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
        var results = new List<QdrantSearchResult>
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
        var results = new List<QdrantSearchResult>
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
        var results = new List<QdrantSearchResult>
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
        var context = new List<QdrantSearchResult>
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
        var context = new List<QdrantSearchResult>
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
        var context = new List<QdrantSearchResult>
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
        var context = new List<QdrantSearchResult>
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
        var context = new List<QdrantSearchResult>
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
}
