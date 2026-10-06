using System.Threading.Channels;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>In-memory notification store for unit tests.</summary>
internal sealed class FakeNotificationRepository : INotificationRepository
{
    public List<Notification> Created { get; } = [];

    public Task CreateAsync(Notification notification, CancellationToken ct = default)
    {
        lock (Created) Created.Add(notification);
        return Task.CompletedTask;
    }

    public Task<List<Notification>> ListRecentAsync(string? userId, int limit, CancellationToken ct = default)
        => Task.FromResult(Created.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).Take(limit).ToList());

    public Task<long> CountUnreadAsync(string? userId, CancellationToken ct = default)
        => Task.FromResult((long)Created.Count(n => n.UserId == userId && !n.IsRead));

    public Task MarkReadAsync(string? userId, IReadOnlyCollection<string>? ids, CancellationToken ct = default)
    {
        foreach (var n in Created.Where(n => n.UserId == userId && (ids is null || ids.Contains(n.Id))))
            n.IsRead = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Issue 048: digest/record split, BuildPrompt truncation faults, single-conversation digest and record
/// generation, de-duplication, digest-triggered re-embedding, opt-in import digests, notifications, and
/// the Markdown export.
/// </summary>
public class DigestRecordAndNotificationTests
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    private static StoredMessage Msg(string role, string text, int i = 0)
        => new() { Id = $"m{i}", Role = role, ContentType = "text", Parts = [text] };

    private static StoredConversation Conversation(string id, params StoredMessage[] messages) => new()
    {
        ConversationId = id,
        Title = $"Title {id}",
        LinearisedMessages = [.. messages],
    };

    private static StoredConversation Simple(string id, ConversationProcessingStatus status = ConversationProcessingStatus.Imported)
    {
        var c = Conversation(id, Msg("user", "How do I prune roses?"), Msg("assistant", "In late winter.", 1));
        c.ProcessingStatus = status;
        return c;
    }

    private static EmbeddingService Embedder(IConversationRepository repo, IVectorStore store)
        => new(repo, new FakeEmbeddingGenerator(TestVector), store, ZeroDelayTimeProvider.Instance,
            Options.Create(new RagOptions()), NullLogger<EmbeddingService>.Instance);

    // ── BuildPrompt faults (regressions) ────────────────────────────────────

    [Fact]
    public void BuildPrompt_FirstVisibleMessageAloneExceedsBudget_ReturnsTruncatedPrompt()
    {
        var conversation = Conversation("c", Msg("user", "START " + new string('x', 50_000)));

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.NotNull(prompt);
        Assert.Contains("user: START xxx", prompt);
        Assert.Contains(SummarisationService.TruncatedMessageSuffix, prompt);
        Assert.True(prompt.Length <= SummarisationService.MaxPromptChars);
    }

    [Fact]
    public void BuildPrompt_OversizedMessageMidStream_StillCollectsLaterMessages()
    {
        var conversation = Conversation("c",
            Msg("user", "opening question", 0),
            Msg("assistant", new string('y', 30_000), 1),
            Msg("user", "later follow-up", 2),
            Msg("assistant", "final answer", 3));

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.NotNull(prompt);
        Assert.Contains("opening question", prompt);
        Assert.Contains("later follow-up", prompt);
        Assert.Contains("final answer", prompt);
    }

    [Fact]
    public void BuildPrompt_LongConversation_KeepsOpeningAndClosing_WithMiddleMarkerBetween()
    {
        var messages = Enumerable.Range(0, 200)
            .Select(i => Msg(i % 2 == 0 ? "user" : "assistant", $"msg-{i:000} " + new string('z', 200), i))
            .ToArray();

        var prompt = SummarisationService.BuildPrompt(Conversation("c", messages))!;

        var first = prompt.IndexOf("msg-000", StringComparison.Ordinal);
        var marker = prompt.IndexOf("from the middle of the conversation omitted", StringComparison.Ordinal);
        var last = prompt.IndexOf("msg-199", StringComparison.Ordinal);
        Assert.True(first >= 0 && marker > first && last > marker, "expected opening, then the marker, then the closing");
        Assert.DoesNotContain("earlier messages truncated", prompt);
        Assert.True(prompt.Length <= SummarisationService.MaxPromptChars);
    }

    [Fact]
    public void BuildPrompt_TruncatedPrompt_KeepsTheWholeFooter()
    {
        var messages = Enumerable.Range(0, 300).Select(i => Msg("user", new string('w', 300), i)).ToArray();

        var prompt = SummarisationService.BuildPrompt(Conversation("c", messages))!;

        Assert.Contains("Write a concise summary (3–6 sentences) that captures:", prompt);
        Assert.EndsWith("Summary:", prompt);
        Assert.True(prompt.Length <= SummarisationService.MaxPromptChars);
    }

    [Fact]
    public async Task Digest_NoVisibleContent_IsRecordedAsSkipped()
    {
        var repo = new FakeConversationRepository();
        var conversation = Conversation("c", new StoredMessage { Id = "h", Role = "system", ContentType = "text", Parts = ["scaffold"], IsHidden = true });
        repo.Seed([conversation]);
        var service = new SummarisationService(repo, new FakeChatClient("unused"), NullLogger<SummarisationService>.Instance);

        var outcome = await service.SummariseConversationAsync("c", embedAfter: false);

        Assert.Equal(DigestOutcome.Skipped, outcome);
        Assert.Equal(ConversationSummaryStatus.Skipped, conversation.SummaryStatus);
        // Skipped conversations are not picked up again by the bulk run.
        Assert.Empty(await repo.GetUnsummarisedAsync(10));
    }

    // ── Digest and record ───────────────────────────────────────────────────

    [Fact]
    public async Task Digest_ForAnAlreadyEmbeddedConversation_IsEmbeddedWithoutAManualStep()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([Simple("c1", ConversationProcessingStatus.Embedded)]);
        var store = new FakeVectorStore();
        var service = new SummarisationService(repo, new FakeChatClient("Pruning roses in winter."),
            NullLogger<SummarisationService>.Instance, Embedder(repo, store));

        var outcome = await service.SummariseConversationAsync("c1", embedAfter: true);

        Assert.Equal(DigestOutcome.Generated, outcome);
        var upsert = Assert.Single(store.Upserted);
        Assert.Equal("Pruning roses in winter.", upsert.Conversation.Summary);
        Assert.Equal(ConversationProcessingStatus.Embedded, (await repo.GetByIdAsync("c1"))!.ProcessingStatus);
        Assert.Equal(0, await repo.CountDigestsAwaitingEmbeddingAsync());
    }

    [Fact]
    public async Task BulkSummarise_EmbedsEachDigest_SoItIsSearchable()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([Simple("c1", ConversationProcessingStatus.Embedded), Simple("c2")]);
        var store = new FakeVectorStore();
        var service = new SummarisationService(repo, new FakeChatClient("A digest."),
            NullLogger<SummarisationService>.Instance, Embedder(repo, store));

        var result = await service.SummariseAsync();

        Assert.Equal(2, result.Summarised);
        Assert.Equal(["c1", "c2"], store.Upserted.Select(u => u.Conversation.ConversationId).Order());
        Assert.All(store.Upserted, u => Assert.Equal("A digest.", u.Conversation.Summary));
    }

    [Fact]
    public async Task DigestsAwaitingEmbedding_AreCounted_AndReconciledByAnEmbedRun()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([Simple("c1", ConversationProcessingStatus.Embedded)]);
        var store = new FakeVectorStore();
        var service = new SummarisationService(repo, new FakeChatClient("A digest."), NullLogger<SummarisationService>.Instance);

        await service.SummariseConversationAsync("c1", embedAfter: false);
        Assert.Equal(1, await repo.CountDigestsAwaitingEmbeddingAsync());

        await Embedder(repo, store).EmbedAsync();

        Assert.Equal(0, await repo.CountDigestsAwaitingEmbeddingAsync());
        Assert.Equal("A digest.", Assert.Single(store.Upserted).Conversation.Summary);
    }

    [Fact]
    public async Task Digest_UsesTheRecordAsInput_WhenOneExists()
    {
        var repo = new FakeConversationRepository();
        var conversation = Simple("c1");
        conversation.Record = "## Roses\n- Decided to prune in late winter.";
        repo.Seed([conversation]);
        string? prompt = null;
        var client = new FakeChatClient(messages =>
        {
            prompt = string.Join("\n", messages.Select(m => m.Text));
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "digest"));
        });
        var service = new SummarisationService(repo, client, NullLogger<SummarisationService>.Instance);

        await service.SummariseConversationAsync("c1", embedAfter: false);

        Assert.Contains("Conversation record:", prompt);
        Assert.Contains("Decided to prune in late winter.", prompt);
        Assert.DoesNotContain("How do I prune roses?", prompt);
    }

    [Fact]
    public async Task Record_IsStored_WithoutTouchingTheDigestStatusOrVectors()
    {
        var repo = new FakeConversationRepository();
        var conversation = Simple("c1", ConversationProcessingStatus.Embedded);
        conversation.Summary = "Existing digest";
        repo.Seed([conversation]);
        var store = new FakeVectorStore();
        var service = new SummarisationService(repo, new FakeChatClient("## Record\n- Pruning"),
            NullLogger<SummarisationService>.Instance, Embedder(repo, store));

        var result = await service.GenerateRecordAsync("c1");

        Assert.Equal("## Record\n- Pruning", result.Record);
        Assert.Equal(("c1", "## Record\n- Pruning"), Assert.Single(repo.RecordUpdates));
        Assert.Empty(repo.SummaryUpdates);
        Assert.Empty(repo.StatusUpdates);
        Assert.Empty(store.Upserted);
        Assert.Equal("Existing digest", conversation.Summary);
        Assert.Equal(ConversationProcessingStatus.Embedded, conversation.ProcessingStatus);
    }

    [Fact]
    public void EmbeddingText_NeverIncludesTheRecord()
    {
        var conversation = Simple("c1");
        conversation.Record = "RECORD-ONLY-TEXT";

        Assert.DoesNotContain("RECORD-ONLY-TEXT", MattGPT.ApiService.Extensions.StoredConversationExtensions.ToEmbeddingText(conversation, 8_000));
    }

    // ── De-duplication ──────────────────────────────────────────────────────

    [Fact]
    public void Queue_DeduplicatesAJobAlreadyQueuedOrRunning_UntilItIsDone()
    {
        var queue = new SummaryJobQueue();
        var record = new SummaryJobRequest(SummaryJobKind.Record, "c1", null);

        Assert.True(queue.TryEnqueue(record));
        Assert.False(queue.TryEnqueue(record with { UserId = "someone-else" }));
        Assert.True(queue.IsPending(SummaryJobKind.Record, "c1"));
        Assert.True(queue.TryEnqueue(new SummaryJobRequest(SummaryJobKind.Digest, "c1", null))); // different job

        queue.MarkDone(record);

        Assert.False(queue.IsPending(SummaryJobKind.Record, "c1"));
        Assert.True(queue.TryEnqueue(record));
    }

    // ── Background jobs and notifications ───────────────────────────────────

    private static (ServiceProvider Provider, FakeConversationRepository Repo, FakeNotificationRepository Notifications, FakeVectorStore Store)
        BuildServices(IChatClient chatClient)
    {
        var repo = new FakeConversationRepository();
        var notifications = new FakeNotificationRepository();
        var store = new FakeVectorStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConversationRepository>(repo);
        services.AddSingleton<INotificationRepository>(notifications);
        services.AddSingleton<IVectorStore>(store);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator(TestVector));
        services.AddSingleton<TimeProvider>(ZeroDelayTimeProvider.Instance);
        services.AddSingleton(Options.Create(new RagOptions()));
        services.AddSingleton(chatClient);
        services.AddScoped<EmbeddingService>();
        services.AddScoped<SummarisationService>();
        services.AddSingleton<NotificationPublisher>();

        return (services.BuildServiceProvider(), repo, notifications, store);
    }

    [Fact]
    public async Task RecordJob_NotifiesTheRequester_WithALinkToTheConversation()
    {
        var (provider, repo, notifications, _) = BuildServices(new FakeChatClient("## Record"));
        repo.Seed([Simple("c1")]);

        await SummaryProcessingService.ProcessAsync(new SummaryJobRequest(SummaryJobKind.Record, "c1", "user-1"), provider, CancellationToken.None);

        var n = Assert.Single(notifications.Created);
        Assert.Equal(NotificationKind.RecordReady, n.Kind);
        Assert.Equal("user-1", n.UserId);
        Assert.Equal("/chat/conversation/c1", n.Link);
        Assert.False(n.IsRead);
    }

    [Fact]
    public async Task RecordJob_Failure_NotifiesFailure()
    {
        var (provider, repo, notifications, _) = BuildServices(new ThrowingChatClient(new HttpRequestException("LLM down")));
        repo.Seed([Simple("c1")]);

        await SummaryProcessingService.ProcessAsync(new SummaryJobRequest(SummaryJobKind.Record, "c1", null), provider, CancellationToken.None);

        Assert.Equal(NotificationKind.RecordFailed, Assert.Single(notifications.Created).Kind);
    }

    [Fact]
    public async Task DigestJob_NotifiesSummaryReady()
    {
        var (provider, repo, notifications, store) = BuildServices(new FakeChatClient("A digest."));
        repo.Seed([Simple("c1", ConversationProcessingStatus.Embedded)]);

        await SummaryProcessingService.ProcessAsync(new SummaryJobRequest(SummaryJobKind.Digest, "c1", null), provider, CancellationToken.None);

        Assert.Equal(NotificationKind.SummaryReady, Assert.Single(notifications.Created).Kind);
        Assert.Single(store.Upserted); // re-embedded with the digest
    }

    // ── Import ──────────────────────────────────────────────────────────────

    private const string TwoConversationExport = """
        [
          { "id": "c1", "title": "T1", "current_node": "n1",
            "mapping": { "n1": { "id": "n1", "parent": null, "children": [],
              "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text", "parts": ["Roses"] } } } } },
          { "id": "c2", "title": "T2", "current_node": "n2",
            "mapping": { "n2": { "id": "n2", "parent": null, "children": [],
              "message": { "id": "n2", "author": { "role": "user" }, "content": { "content_type": "text", "parts": ["Tulips"] } } } } }
        ]
        """;

    private static async Task<ImportJob> RunImportAsync(
        ServiceProvider provider, FakeConversationRepository repo, string json, bool generateSummaries)
    {
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var service = new ImportProcessingService(channel, store, new ConversationParser(), repo,
            new FakeUserProfileRepository(), provider, NullLogger<ImportProcessingService>.Instance);

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);
        var job = store.CreateJob();
        job.FileName = "conversations.json";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath, "user-1", generateSummaries), cts.Token);

        // CompletedAt is set last, after digests, embedding and the notification.
        while (job.CompletedAt is null)
            await Task.Delay(20, cts.Token);

        cts.Cancel();
        try { await service.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
        return job;
    }

    /// <summary>Chat client that counts calls, so tests can prove no LLM work happened.</summary>
    private sealed class CountingChatClient(string reply) : IChatClient
    {
        public int Calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? key = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Import_WithoutGenerateSummaries_MakesNoLlmCalls_AndNotifiesCompletion()
    {
        var client = new CountingChatClient("unused");
        var (provider, repo, notifications, store) = BuildServices(client);

        var job = await RunImportAsync(provider, repo, TwoConversationExport, generateSummaries: false);

        Assert.Equal(0, client.Calls);
        Assert.Equal(SummaryJobStatus.NotRequested, job.SummaryStatus);
        Assert.Empty(repo.SummaryUpdates);
        Assert.Equal(2, store.Upserted.Count);
        Assert.All(store.Upserted, u => Assert.Null(u.Conversation.Summary));

        var n = Assert.Single(notifications.Created);
        Assert.Equal(NotificationKind.ImportCompleted, n.Kind);
        Assert.Equal("user-1", n.UserId);
        Assert.DoesNotContain("summarised", n.Message);
    }

    [Fact]
    public async Task Import_WithGenerateSummaries_DigestsEachConversationBeforeEmbedding()
    {
        var client = new CountingChatClient("A digest.");
        var (provider, repo, notifications, store) = BuildServices(client);

        var job = await RunImportAsync(provider, repo, TwoConversationExport, generateSummaries: true);

        Assert.Equal(SummaryJobStatus.Complete, job.SummaryStatus);
        Assert.Equal(2, job.SummarisedConversations);
        Assert.Equal(2, client.Calls);
        // Every embedding includes its digest, so no digest is left awaiting embedding.
        Assert.Equal(2, store.Upserted.Count);
        Assert.All(store.Upserted, u => Assert.Equal("A digest.", u.Conversation.Summary));
        Assert.Equal(0, await repo.CountDigestsAwaitingEmbeddingAsync());

        var n = Assert.Single(notifications.Created);
        Assert.Equal(NotificationKind.ImportCompleted, n.Kind);
        Assert.Contains("2 summarised, 0 skipped, 0 summary failure(s)", n.Message);
    }

    [Fact]
    public async Task Import_DigestFailures_DoNotFailTheImport_OrBlockEmbedding()
    {
        var (provider, repo, notifications, store) = BuildServices(new ThrowingChatClient(new HttpRequestException("LLM down")));

        var job = await RunImportAsync(provider, repo, TwoConversationExport, generateSummaries: true);

        Assert.Equal(ImportJobStatus.Complete, job.Status);
        Assert.Equal(2, job.SummaryErrors);
        Assert.Equal(2, store.Upserted.Count);
        Assert.Contains("2 summary failure(s)", Assert.Single(notifications.Created).Message);
    }

    [Fact]
    public async Task Import_Failure_NotifiesImportFailed()
    {
        var (provider, repo, notifications, _) = BuildServices(new FakeChatClient("unused"));

        var job = await RunImportAsync(provider, repo, "NOT JSON {{{", generateSummaries: false);

        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(NotificationKind.ImportFailed, Assert.Single(notifications.Created).Kind);
    }

    // ── Export ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Plans: A/B testing?", "Plans- A-B testing.md")]
    [InlineData("  ..Spaced   out..  ", "Spaced out.md")]
    [InlineData("<>:\"/\\|?*", "conversation.md")]
    [InlineData(null, "conversation.md")]
    public void FileName_IsSafeForFileSystems(string? title, string expected)
        => Assert.Equal(expected, ConversationRecordExport.FileName(title));

    [Fact]
    public void FileName_LongTitle_IsCut()
        => Assert.True(ConversationRecordExport.FileName(new string('a', 500)).Length <= ConversationRecordExport.MaxFileNameStemChars + 3);

    [Fact]
    public void Markdown_HasTitleHeadingDateAndRecord()
    {
        var conversation = Simple("c1");
        conversation.Title = "Rose pruning";
        conversation.CreateTime = new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        conversation.Record = "## Decisions\n- Prune in late winter.";

        var markdown = ConversationRecordExport.BuildMarkdown(conversation);

        Assert.StartsWith("# Rose pruning\n", markdown.ReplaceLineEndings("\n"));
        Assert.Contains("*5 March 2024*", markdown);
        Assert.Contains("- Prune in late winter.", markdown);
    }
}
