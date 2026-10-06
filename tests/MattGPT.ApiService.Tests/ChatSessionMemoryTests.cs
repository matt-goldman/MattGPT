using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>Current user stub with a fixed user id.</summary>
internal sealed class FixedCurrentUserService(string? userId) : ICurrentUserService
{
    public string? UserId => userId;
}

/// <summary>TimeProvider whose clock is fixed, so idle timeouts are deterministic.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Chat session lifecycle and memory (issue 049, ADR-013): completion, reactivation, projection,
/// embedding, self-exclusion from retrieval, and user scoping.
/// </summary>
public class ChatSessionMemoryTests
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static ChatSession MakeSession(
        string? userId = null,
        ChatSessionStatus status = ChatSessionStatus.Active,
        ChatSessionEmbeddingStatus embeddingStatus = ChatSessionEmbeddingStatus.None,
        DateTimeOffset? updatedAt = null,
        int messageCount = 2)
        => new()
        {
            Title = "Planning the garden",
            UserId = userId,
            Status = status,
            EmbeddingStatus = embeddingStatus,
            CreatedAt = (updatedAt ?? Now) - TimeSpan.FromMinutes(10),
            UpdatedAt = updatedAt ?? Now,
            Messages = [.. Enumerable.Range(0, messageCount).Select(i => new ChatSessionMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Garden message {i}",
                Timestamp = Now,
            })],
        };

    private sealed record Harness(
        ChatSessionMemoryService Memory,
        FakeChatSessionRepository Sessions,
        FakeConversationRepository Conversations,
        IVectorStore VectorStore);

    private static Harness CreateMemory(
        FakeChatSessionRepository? sessions = null,
        IVectorStore? vectorStore = null,
        IChatClient? chatClient = null)
    {
        sessions ??= new FakeChatSessionRepository();
        var conversations = new FakeConversationRepository();
        vectorStore ??= new FakeVectorStore();

        var embedder = new EmbeddingService(
            conversations, new FakeEmbeddingGenerator(TestVector), vectorStore, ZeroDelayTimeProvider.Instance,
            Options.Create(new RagOptions()), NullLogger<EmbeddingService>.Instance);
        var summariser = new SummarisationService(
            conversations, chatClient ?? new FakeChatClient("The user planned a vegetable garden."),
            NullLogger<SummarisationService>.Instance);

        var memory = new ChatSessionMemoryService(
            sessions, conversations, summariser, embedder,
            Options.Create(new ChatSessionOptions { IdleTimeout = TimeSpan.FromMinutes(30) }),
            new FixedTimeProvider(Now),
            NullLogger<ChatSessionMemoryService>.Instance);

        return new Harness(memory, sessions, conversations, vectorStore);
    }

    private static ChatSessionService CreateSessionService(
        FakeChatSessionRepository repo, string? userId = null, ChatSessionMemorySignal? signal = null)
        => new(
            repo,
            new FakeChatClient("unused"),
            Options.Create(new ChatSessionOptions()),
            new FixedCurrentUserService(userId),
            NullLogger<ChatSessionService>.Instance,
            signal);

    // ── Projection ──────────────────────────────────────────────────────────

    [Fact]
    public void FromChatSession_ProjectsSessionAsOwnedChatSessionConversation()
    {
        var session = MakeSession(userId: "user-1");

        var projection = StoredConversation.FromChatSession(session, "A summary");

        Assert.Equal(session.SessionId.ToString(), projection.ConversationId);
        Assert.True(Guid.TryParse(projection.ConversationId, out _)); // vector stores (Qdrant) need a UUID id
        Assert.Equal(ConversationSource.ChatSession, projection.Source);
        Assert.Equal("user-1", projection.UserId);
        Assert.Equal("Planning the garden", projection.Title);
        Assert.Equal("A summary", projection.Summary);
        // Summarised keeps the bulk summariser (which takes Imported) away from projections.
        Assert.Equal(ConversationProcessingStatus.Summarised, projection.ProcessingStatus);
        Assert.Equal(["Garden message 0", "Garden message 1"], projection.LinearisedMessages.SelectMany(m => m.Parts));
        Assert.Equal(["user", "assistant"], projection.LinearisedMessages.Select(m => m.Role));
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NewChat_CompletesTheUsersPreviousActiveSessions_AndWakesTheSweep()
    {
        var repo = new FakeChatSessionRepository();
        var previous = MakeSession(userId: "user-1");
        var otherUsers = MakeSession(userId: "user-2");
        repo.Seed(previous);
        repo.Seed(otherUsers);
        var signal = new ChatSessionMemorySignal();

        var created = await CreateSessionService(repo, "user-1", signal).GetOrCreateAsync(null);

        Assert.Equal(ChatSessionStatus.Completed, previous.Status);
        Assert.Equal(ChatSessionEmbeddingStatus.Pending, previous.EmbeddingStatus);
        Assert.Equal(ChatSessionStatus.Active, otherUsers.Status);
        Assert.Equal(ChatSessionStatus.Active, created.Status);

        var woke = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Same(woke, await Task.WhenAny(woke, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task ContinuingACompletedSession_ReactivatesIt_AndClearsItsStaleSummary()
    {
        var repo = new FakeChatSessionRepository();
        var session = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Embedded);
        session.Summary = "Old summary";
        repo.Seed(session);
        var service = CreateSessionService(repo);

        await service.AddUserMessageAsync(session, "One more thing about the garden");

        var stored = await repo.GetByIdAsync(session.SessionId);
        Assert.Equal(ChatSessionStatus.Active, stored!.Status);
        Assert.Null(stored.Summary);
        Assert.Equal(ChatSessionEmbeddingStatus.None, stored.EmbeddingStatus);
    }

    [Fact]
    public async Task Sweep_CompletesIdleSessions_AndLeavesRecentOnesActive()
    {
        var sessions = new FakeChatSessionRepository();
        var idle = MakeSession(updatedAt: Now - TimeSpan.FromHours(2));
        var recent = MakeSession(updatedAt: Now - TimeSpan.FromMinutes(5));
        sessions.Seed(idle);
        sessions.Seed(recent);
        var h = CreateMemory(sessions);

        var result = await h.Memory.SweepAsync(retryErrors: false);

        Assert.Equal(1, result.Completed);
        Assert.Equal(ChatSessionStatus.Completed, idle.Status);
        Assert.Equal(ChatSessionEmbeddingStatus.Embedded, idle.EmbeddingStatus);
        Assert.Equal(ChatSessionStatus.Active, recent.Status);
        Assert.Equal(ChatSessionEmbeddingStatus.None, recent.EmbeddingStatus);
    }

    // ── Embedding ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Sweep_StoresPendingSession_AsAnEmbeddedProjectionWithAWholeSessionSummary()
    {
        var sessions = new FakeChatSessionRepository();
        var session = MakeSession(userId: "user-1", status: ChatSessionStatus.Completed,
            embeddingStatus: ChatSessionEmbeddingStatus.Pending);
        sessions.Seed(session);
        var h = CreateMemory(sessions);

        var result = await h.Memory.SweepAsync(retryErrors: false);

        Assert.Equal(1, result.Embedded);
        Assert.Equal(ChatSessionEmbeddingStatus.Embedded, session.EmbeddingStatus);
        Assert.Equal("The user planned a vegetable garden.", session.Summary);

        var projection = Assert.Single(h.Conversations.Upserted);
        Assert.Equal(session.SessionId.ToString(), projection.ConversationId);
        Assert.Equal(ConversationSource.ChatSession, projection.Source);
        Assert.Equal("user-1", projection.UserId);
        Assert.Equal("The user planned a vegetable garden.", projection.Summary);

        var upserted = Assert.Single(((FakeVectorStore)h.VectorStore).Upserted);
        Assert.Equal(session.SessionId.ToString(), upserted.Conversation.ConversationId);
        Assert.Equal("user-1", upserted.Conversation.UserId);
        Assert.Contains(h.Conversations.StatusUpdates,
            u => u.Id == session.SessionId.ToString() && u.Status == ConversationProcessingStatus.Embedded);
    }

    [Fact]
    public async Task Store_SummaryFails_StillEmbedsWithoutSummary()
    {
        var sessions = new FakeChatSessionRepository();
        var session = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Pending);
        sessions.Seed(session);
        var h = CreateMemory(sessions, chatClient: new ThrowingChatClient(new HttpRequestException("LLM down")));

        var embedded = await h.Memory.StoreAsync(session);

        Assert.True(embedded);
        Assert.Equal(ChatSessionEmbeddingStatus.Embedded, session.EmbeddingStatus);
        Assert.Null(session.Summary);
        Assert.Single(((FakeVectorStore)h.VectorStore).Upserted);
    }

    [Fact]
    public async Task Sweep_EmbeddingFails_MarksError_WhichOnlyTheBackfillRetries()
    {
        var sessions = new FakeChatSessionRepository();
        var session = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Pending);
        sessions.Seed(session);
        var failing = CreateMemory(sessions, new ThrowingVectorStore());

        var first = await failing.Memory.SweepAsync(retryErrors: false);
        Assert.Equal(1, first.Errors);
        Assert.Equal(ChatSessionEmbeddingStatus.Error, session.EmbeddingStatus);

        // The scheduled sweep leaves failures alone...
        var working = CreateMemory(sessions);
        var scheduled = await working.Memory.SweepAsync(retryErrors: false);
        Assert.Equal(0, scheduled.Embedded + scheduled.Errors);

        // ...the backfill retries them.
        var backfill = await working.Memory.SweepAsync(retryErrors: true);
        Assert.Equal(1, backfill.Embedded);
        Assert.Equal(ChatSessionEmbeddingStatus.Embedded, session.EmbeddingStatus);
    }

    [Fact]
    public async Task ResumedSession_IsReEmbeddedWithItsNewMessages_WhenItCompletesAgain()
    {
        var sessions = new FakeChatSessionRepository();
        var session = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Pending);
        sessions.Seed(session);
        var h = CreateMemory(sessions);
        await h.Memory.SweepAsync(retryErrors: false);

        // Continue it, then start another chat, which completes it again.
        var service = CreateSessionService(sessions);
        await service.AddUserMessageAsync(session, "Add tomatoes to the plan");
        Assert.Equal(ChatSessionStatus.Active, session.Status);
        await service.GetOrCreateAsync(null);
        Assert.Equal(ChatSessionEmbeddingStatus.Pending, session.EmbeddingStatus);

        await h.Memory.SweepAsync(retryErrors: false);

        var upserts = ((FakeVectorStore)h.VectorStore).Upserted;
        Assert.Equal(2, upserts.Count);
        Assert.All(upserts, u => Assert.Equal(session.SessionId.ToString(), u.Conversation.ConversationId));
        Assert.Contains("Add tomatoes to the plan", upserts[1].Conversation.LinearisedMessages.SelectMany(m => m.Parts));
        Assert.Equal(ChatSessionEmbeddingStatus.Embedded, session.EmbeddingStatus);
    }

    [Fact]
    public async Task Store_SessionContinuedWhileBeingStored_IsNotMarkedEmbedded()
    {
        var sessions = new FakeChatSessionRepository();
        var current = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Pending, messageCount: 4);
        sessions.Seed(current);
        var h = CreateMemory(sessions);

        // The copy being stored was read before two more messages arrived.
        var staleSnapshot = MakeSession(status: ChatSessionStatus.Completed, embeddingStatus: ChatSessionEmbeddingStatus.Pending, messageCount: 2);
        staleSnapshot.SessionId = current.SessionId;

        await h.Memory.StoreAsync(staleSnapshot);

        Assert.Equal(ChatSessionEmbeddingStatus.Pending, current.EmbeddingStatus);
    }

    [Fact]
    public async Task BulkEmbed_SkipsChatSessionProjections()
    {
        var repo = new FakeConversationRepository();
        var projection = StoredConversation.FromChatSession(MakeSession(), "summary");
        var import = new StoredConversation
        {
            ConversationId = "import-1",
            Title = "Imported",
            LinearisedMessages = [new StoredMessage { Id = "m0", Role = "user", ContentType = "text", Parts = ["hello"] }],
        };
        repo.Seed([projection, import]);
        var vectorStore = new FakeVectorStore();
        var service = new EmbeddingService(repo, new FakeEmbeddingGenerator(TestVector), vectorStore,
            ZeroDelayTimeProvider.Instance, Options.Create(new RagOptions()), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal("import-1", Assert.Single(vectorStore.Upserted).Conversation.ConversationId);
    }

    // ── Retrieval ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Retriever_ExcludesTheCurrentSession_AndStillFillsTheLimit()
    {
        var store = new FakeSearchVectorStore(
        [
            new VectorSearchResult("self", 0.95f, "This chat", null),
            new VectorSearchResult("c1", 0.9f, "C1", null),
            new VectorSearchResult("c2", 0.8f, "C2", null),
        ]);

        var result = await TestRetriever.Create(store)
            .RetrieveAsync("query", limit: 2, minScore: 0.5f, excludeConversationId: "self");

        Assert.Equal(["c1", "c2"], result.Results.Select(r => r.ConversationId));
    }

    [Fact]
    public async Task KeywordTool_ExcludesTheCurrentSession()
    {
        var repo = new FakeConversationRepository();
        var current = MakeSession();
        repo.Seed([
            StoredConversation.FromChatSession(current, null),
            new StoredConversation
            {
                ConversationId = "import-1",
                Title = "Imported garden notes",
                LinearisedMessages = [new StoredMessage { Id = "m0", Role = "user", ContentType = "text", Parts = ["Garden beds"] }],
            },
        ]);
        var tool = new KeywordSearchMemoriesTool(repo, Options.Create(new RagOptions()), new NullCurrentUserService(),
            NullLogger<KeywordSearchMemoriesTool>.Instance)
        {
            ExcludeConversationId = current.SessionId.ToString(),
        };

        await tool.SearchMemoriesKeywordAsync("Garden", maxResults: 1);

        Assert.Equal("import-1", Assert.Single(tool.Sources).ConversationId);
    }

    [Fact]
    public async Task Chat_DoesNotRetrieveTheCurrentSession_AndMarksEarlierSessionsAsChatSources()
    {
        var current = MakeSession();
        var earlier = MakeSession();
        var repo = new FakeConversationRepository();
        repo.Seed([StoredConversation.FromChatSession(current, null), StoredConversation.FromChatSession(earlier, null)]);

        var store = new FakeSearchVectorStore(
        [
            new VectorSearchResult(current.SessionId.ToString(), 0.95f, "Current", null),
            new VectorSearchResult(earlier.SessionId.ToString(), 0.9f, "Earlier", null),
        ]);
        var rag = new RagService(
            TestRetriever.Create(store, repo),
            new FakeChatClient("Answer"),
            Options.Create(new RagOptions { Mode = RagMode.WithPrompt, TopK = 5, MinScore = 0.5f }),
            Options.Create(new ChatSessionOptions()),
            NullLogger<RagService>.Instance);

        var response = await rag.ChatAsync("garden", current);

        var source = Assert.Single(response.Sources);
        Assert.Equal(earlier.SessionId.ToString(), source.ConversationId);
        Assert.Equal(ConversationSource.ChatSession, source.Source);
    }

    [Fact]
    public async Task Retriever_ScopesTheVectorSearchToTheCurrentUser()
    {
        var store = new UserRecordingVectorStore();
        var retriever = new MemoryRetriever(
            new FakeEmbeddingGenerator(TestVector), store, new FakeConversationRepository(),
            new FixedCurrentUserService("user-1"), Options.Create(new RagOptions()),
            NullLogger<MemoryRetriever>.Instance);

        await retriever.RetrieveAsync("query", 5, 0.5f);

        Assert.Equal("user-1", store.LastUserId);
    }

    private sealed class UserRecordingVectorStore : IVectorStore
    {
        public string? LastUserId { get; private set; }

        public Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        {
            LastUserId = userId;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        public Task<ulong?> GetPointCountAsync(CancellationToken ct = default) => Task.FromResult<ulong?>(0);
    }
}
