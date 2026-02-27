using MattGPT.ApiService;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// In-memory fake of <see cref="IChatSessionRepository"/> for unit tests.
/// </summary>
internal sealed class FakeChatSessionRepository : IChatSessionRepository
{
    private readonly Dictionary<Guid, ChatSession> _sessions = new();

    public List<ChatSession> CreatedSessions { get; } = [];

    public Task CreateAsync(ChatSession session, CancellationToken ct = default)
    {
        _sessions[session.SessionId] = session;
        CreatedSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<ChatSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task AddMessageAsync(Guid sessionId, ChatSessionMessage message, CancellationToken ct = default)
    {
        // The in-memory session object is already updated by the service before this call.
        return Task.CompletedTask;
    }

    public Task UpdateTitleAsync(Guid sessionId, string title, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            session.Title = title;
        return Task.CompletedTask;
    }

    public Task UpdateRollingSummaryAsync(Guid sessionId, string summary, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            session.RollingSummary = summary;
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid sessionId, ChatSessionStatus status, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            session.Status = status;
        return Task.CompletedTask;
    }

    /// <summary>Seed a pre-existing session for tests that need to start with state.</summary>
    public void Seed(ChatSession session) => _sessions[session.SessionId] = session;
}

public class ChatSessionServiceTests
{
    private static ChatSessionService CreateService(
        FakeChatSessionRepository? repo = null,
        string llmSummaryResponse = "Summary of conversation.",
        ChatSessionOptions? options = null)
    {
        repo ??= new FakeChatSessionRepository();
        var opts = Options.Create(options ?? new ChatSessionOptions
        {
            MaxConversationTokens = 2048,
            RecentMessageCount = 6,
            SummaryPrompt = "Summarise the conversation so far.",
        });
        return new ChatSessionService(
            repo,
            new FakeChatClient(llmSummaryResponse),
            opts,
            NullLogger<ChatSessionService>.Instance);
    }

    [Fact]
    public async Task GetOrCreateAsync_NullSessionId_CreatesNewSession()
    {
        var repo = new FakeChatSessionRepository();
        var service = CreateService(repo);

        var session = await service.GetOrCreateAsync(null);

        Assert.NotEqual(Guid.Empty, session.SessionId);
        Assert.Single(repo.CreatedSessions);
    }

    [Fact]
    public async Task GetOrCreateAsync_ExistingSessionId_ReturnsSameSession()
    {
        var repo = new FakeChatSessionRepository();
        var existing = new ChatSession { SessionId = Guid.NewGuid() };
        repo.Seed(existing);
        var service = CreateService(repo);

        var session = await service.GetOrCreateAsync(existing.SessionId);

        Assert.Equal(existing.SessionId, session.SessionId);
        Assert.Empty(repo.CreatedSessions); // should NOT create a new one
    }

    [Fact]
    public async Task GetOrCreateAsync_UnknownSessionId_CreatesNewSession()
    {
        var repo = new FakeChatSessionRepository();
        var service = CreateService(repo);

        var session = await service.GetOrCreateAsync(Guid.NewGuid());

        Assert.Single(repo.CreatedSessions);
    }

    [Fact]
    public async Task AddUserMessageAsync_AppendsMessageToSession()
    {
        var service = CreateService();
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "Hello!");

        Assert.Single(session.Messages);
        Assert.Equal("user", session.Messages[0].Role);
        Assert.Equal("Hello!", session.Messages[0].Content);
    }

    [Fact]
    public async Task AddUserMessageAsync_SetsTitle_FromFirstMessage()
    {
        var service = CreateService();
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "What is Qdrant?");

        Assert.Equal("What is Qdrant?", session.Title);
    }

    [Fact]
    public async Task AddUserMessageAsync_TruncatesLongTitle()
    {
        var service = CreateService();
        var session = await service.GetOrCreateAsync(null);

        var longMessage = new string('a', 200);
        await service.AddUserMessageAsync(session, longMessage);

        Assert.True(session.Title!.Length <= 81); // 80 chars + ellipsis
        Assert.EndsWith("…", session.Title);
    }

    [Fact]
    public async Task AddAssistantMessageAsync_AppendsAssistantMessage()
    {
        var service = CreateService();
        var session = await service.GetOrCreateAsync(null);

        await service.AddAssistantMessageAsync(session, "I can help with that.");

        Assert.Single(session.Messages);
        Assert.Equal("assistant", session.Messages[0].Role);
    }

    [Fact]
    public async Task GetRecentMessages_ReturnsLastNMessages()
    {
        var service = CreateService(options: new ChatSessionOptions { RecentMessageCount = 2 });
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "msg 1");
        await service.AddAssistantMessageAsync(session, "reply 1");
        await service.AddUserMessageAsync(session, "msg 2");
        await service.AddAssistantMessageAsync(session, "reply 2");

        var recent = service.GetRecentMessages(session);

        Assert.Equal(2, recent.Count);
        Assert.Equal("msg 2", recent[0].Content);
        Assert.Equal("reply 2", recent[1].Content);
    }

    [Fact]
    public async Task GetRecentMessages_FewerMessagesThanWindow_ReturnsAll()
    {
        var service = CreateService(options: new ChatSessionOptions { RecentMessageCount = 10 });
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "only message");

        var recent = service.GetRecentMessages(session);

        Assert.Single(recent);
    }

    [Fact]
    public void EstimateTokens_CalculatesCorrectly()
    {
        Assert.Equal(0, ChatSessionService.EstimateTokens(null));
        Assert.Equal(0, ChatSessionService.EstimateTokens(""));
        Assert.Equal(5, ChatSessionService.EstimateTokens("12345678901234567890")); // 20 chars / 4
    }

    [Fact]
    public async Task RollingSummary_NotTriggered_WhenUnderBudget()
    {
        var repo = new FakeChatSessionRepository();
        var service = CreateService(repo, options: new ChatSessionOptions
        {
            MaxConversationTokens = 10000, // very high budget
            RecentMessageCount = 2,
        });
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "Short message");

        Assert.Null(session.RollingSummary);
    }

    [Fact]
    public async Task RollingSummary_Triggered_WhenOverBudget()
    {
        var repo = new FakeChatSessionRepository();
        var summaryText = "This is the rolling summary.";
        var service = CreateService(repo, llmSummaryResponse: summaryText, options: new ChatSessionOptions
        {
            MaxConversationTokens = 10, // very low budget — 40 chars
            RecentMessageCount = 2,
        });
        var session = await service.GetOrCreateAsync(null);

        // Add enough messages to exceed the budget.
        await service.AddUserMessageAsync(session, "This is a long enough user message to exceed the tiny budget");
        await service.AddAssistantMessageAsync(session, "And here is a long assistant reply as well to push over");
        await service.AddUserMessageAsync(session, "Another user message that will definitely trigger summarisation");

        Assert.Equal(summaryText, session.RollingSummary);
    }

    [Fact]
    public async Task RollingSummary_NotTriggered_WhenAllMessagesInRecentWindow()
    {
        var repo = new FakeChatSessionRepository();
        var service = CreateService(repo, options: new ChatSessionOptions
        {
            MaxConversationTokens = 1, // impossibly low budget
            RecentMessageCount = 100, // but huge recent window
        });
        var session = await service.GetOrCreateAsync(null);

        await service.AddUserMessageAsync(session, "This is a long message that exceeds the budget");

        Assert.Null(session.RollingSummary); // can't summarise if everything is "recent"
    }
}
