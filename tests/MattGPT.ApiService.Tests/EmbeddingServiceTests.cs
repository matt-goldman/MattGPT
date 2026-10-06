using System.Net;
using MattGPT.ApiService.Extensions;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IEmbeddingGenerator that returns a fixed embedding vector or throws on demand.
/// </summary>
internal sealed class FakeEmbeddingGenerator(Func<IEnumerable<string>, IList<Embedding<float>>> handler) : IEmbeddingGenerator<string, Embedding<float>>
{
    public FakeEmbeddingGenerator(float[] vector)
        : this(_ => [new Embedding<float>(vector)]) { }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(handler(values)));

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Fake IEmbeddingGenerator that always throws an exception.
/// </summary>
internal sealed class ThrowingEmbeddingGenerator(Exception exception) : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw exception;

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Fake IVectorStore that records upserts for assertion in tests.
/// </summary>
internal sealed class FakeVectorStore : IVectorStore
{
    public List<(StoredConversation Conversation, float[] Vector)> Upserted { get; } = [];

    public Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
    {
        Upserted.Add((conversation, vector));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>((ulong)Upserted.Count);
}

/// <summary>
/// Fake IVectorStore that always throws on upsert.
/// </summary>
internal sealed class ThrowingVectorStore : IVectorStore
{
    public Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
        => throw new InvalidOperationException("Vector store unavailable");

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>(null);
}

/// <summary>
/// TimeProvider that completes delays immediately, avoiding real waits in tests.
/// </summary>
internal sealed class ZeroDelayTimeProvider : TimeProvider
{
    public static readonly ZeroDelayTimeProvider Instance = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Fire the callback synchronously and return a no-op timer.
        callback(state);
        return new NoOpTimer();
    }

    private sealed class NoOpTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}

public class EmbeddingServiceTests
{
    private static StoredConversation MakeConversation(
        string id,
        string? title = "Test",
        string? summary = null,
        ConversationProcessingStatus status = ConversationProcessingStatus.Imported,
        int messageCount = 2)
    {
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = i % 2 == 0 ? "user" : "assistant",
                ContentType = "text",
                Parts = [$"Message content {i}"],
            })
            .ToList();

        return new StoredConversation
        {
            ConversationId = id,
            Title = title,
            Summary = summary,
            ProcessingStatus = status,
            LinearisedMessages = messages,
        };
    }

    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    /// <summary>RAG options without reranking, so embedding uses the standard text length.</summary>
    private static readonly IOptions<RagOptions> StandardRagOptions =
        Options.Create(new RagOptions { UseReranking = false });

    /// <summary>
    /// Builds a conversation whose embedding text is roughly <paramref name="approxChars"/> long.
    /// </summary>
    private static StoredConversation MakeLongConversation(string id, int approxChars)
    {
        const string body = "This is a fairly long message used to build up a long conversation for embedding.";
        var count = approxChars / (body.Length + 10) + 1;

        return new StoredConversation
        {
            ConversationId = id,
            Title = "Long",
            ProcessingStatus = ConversationProcessingStatus.Imported,
            LinearisedMessages = [.. Enumerable.Range(0, count)
                .Select(i => new StoredMessage
                {
                    Id = $"m{i}",
                    Role = "user",
                    ContentType = "text",
                    Parts = [body],
                })],
        };
    }

    [Fact]
    public async Task EmbedAsync_ImportedConversation_UpdatesStatusToEmbedded()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
        Assert.Single(repository.StatusUpdates);
        Assert.Equal("c1", repository.StatusUpdates[0].Id);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.StatusUpdates[0].Status);
        Assert.Equal(TestVector, qdrant.Upserted[0].Vector);
        Assert.Single(qdrant.Upserted);
        Assert.Equal("c1", qdrant.Upserted[0].Conversation.ConversationId);
    }

    [Fact]
    public async Task EmbedAsync_SummarisedConversation_StillGetsEmbedded()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", summary: "A test summary.", status: ConversationProcessingStatus.Summarised)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Single(qdrant.Upserted);
    }

    [Fact]
    public async Task EmbedAsync_EmbeddingError_MarksAsEmbeddingError()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new ThrowingEmbeddingGenerator(new InvalidOperationException("Model unavailable"));
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Single(repository.StatusUpdates);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_PreviouslyFailedConversation_IsRetried()
    {
        // Regression: conversations that failed to embed on import are left as EmbeddingError.
        // A re-run must pick them up and retry them, otherwise failures are never recovered.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", status: ConversationProcessingStatus.EmbeddingError)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Single(repository.StatusUpdates);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_PersistentErrors_AttemptEachOnceAndTerminate()
    {
        // Regression: failures are re-marked EmbeddingError, which is itself an embeddable status.
        // Each conversation must be attempted exactly once per run so a persistent failure doesn't
        // get re-fetched forever (which previously spun EmbedAsync into an infinite loop). The count
        // spans several internal batches to prove already-attempted conversations don't crowd out
        // not-yet-attempted ones (starvation) — every conversation must be reached.
        const int count = 120;
        var repository = new FakeConversationRepository();
        repository.Seed(Enumerable.Range(0, count).Select(i => MakeConversation($"c{i}")));

        var generator = new ThrowingEmbeddingGenerator(new InvalidOperationException("Model unavailable"));
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(count, result.Errors);
        // Exactly one update per conversation — proof each was attempted once and the loop ended.
        Assert.Equal(count, repository.StatusUpdates.Count);
        Assert.Equal(count, repository.StatusUpdates.Select(u => u.Id).Distinct().Count());
        Assert.All(repository.StatusUpdates, u => Assert.Equal(ConversationProcessingStatus.EmbeddingError, u.Status));
    }

    [Fact]
    public async Task EmbedAsync_NoContentAtAll_MarksAsEmbeddedSkipped()
    {
        // A conversation with no title, no summary, and no messages has nothing to embed.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", title: null, summary: null, messageCount: 0)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Single(repository.StatusUpdates);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_NoSummaryButHasMessages_StillEmbeds()
    {
        // Conversations without a summary should still be embedded from their message content.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", summary: null, messageCount: 3)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Skipped);
        Assert.Single(qdrant.Upserted);
    }

    [Fact]
    public async Task EmbedAsync_MultipleConversations_AllProcessed()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeConversation("c1"),
            MakeConversation("c2"),
            MakeConversation("c3"),
        ]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(_ =>
        {
            callCount++;
            return [new Embedding<float>(TestVector)];
        });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(3, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task EmbedAsync_MixedImportedAndSummarised_BothProcessed()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeConversation("c1", status: ConversationProcessingStatus.Imported),
            MakeConversation("c2", summary: "Has a summary", status: ConversationProcessingStatus.Summarised),
        ]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(2, result.Embedded);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public async Task EmbedAsync_ErrorDoesNotAbortBatch()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeConversation("c1"),
            MakeConversation("c2"),
            MakeConversation("c3"),
        ]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(_ =>
        {
            callCount++;
            if (callCount == 2)
                throw new InvalidOperationException("Embedding error on second call");
            return [new Embedding<float>(TestVector)];
        });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(2, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(3, callCount);
        Assert.Equal(3, repository.StatusUpdates.Count);
    }

    [Fact]
    public async Task EmbedAsync_EmptyRepository_ReturnsZeroCounts()
    {
        var repository = new FakeConversationRepository();
        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task EmbedAsync_SuccessfulEmbedding_UpsertsToQdrant()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var qdrant = new FakeVectorStore();
        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        await service.EmbedAsync();

        Assert.Single(qdrant.Upserted);
        Assert.Equal("c1", qdrant.Upserted[0].Conversation.ConversationId);
        Assert.Equal(TestVector, qdrant.Upserted[0].Vector);
    }

    [Fact]
    public async Task EmbedAsync_VectorStoreFails_MarksEmbeddingErrorForRetry()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new ThrowingVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        // The vector store is the only place the vector is kept, so a failed upsert must not
        // leave the conversation marked Embedded (it would never be retried or found).
        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Single(repository.StatusUpdates);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_EmptyConversation_DoesNotUpsertToQdrant()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", title: null, summary: null, messageCount: 0)]);

        var qdrant = new FakeVectorStore();
        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        await service.EmbedAsync();

        // No embeddable content means nothing to upsert to Qdrant.
        Assert.Empty(qdrant.Upserted);
    }

    [Fact]
    public void ToEmbeddingText_IncludesTitleAndMessages()
    {
        var conv = MakeConversation("c1", title: "My Topic", messageCount: 2);

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.Contains("My Topic", text);
        Assert.Contains("user: Message content 0", text);
        Assert.Contains("assistant: Message content 1", text);
    }

    [Fact]
    public void ToEmbeddingText_WithSummary_IncludesSummary()
    {
        var conv = MakeConversation("c1", title: "My Topic", summary: "This is a summary.", messageCount: 1);

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.Contains("This is a summary.", text);
        Assert.Contains("My Topic", text);
    }

    [Fact]
    public void ToEmbeddingText_TruncatesLongConversations()
    {
        var conv = new StoredConversation
        {
            ConversationId = "long",
            Title = "Long",
            LinearisedMessages = [.. Enumerable.Range(0, 500)
                .Select(i => new StoredMessage
                {
                    Id = $"m{i}",
                    Role = "user",
                    ContentType = "text",
                    Parts = [$"This is a fairly long message number {i} to eventually exceed the limit."],
                })],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.True(text.Length <= EmbeddingService.MaxEmbeddingTextChars);
    }

    [Fact]
    public void ToEmbeddingText_ExcludesZeroWeightMessages()
    {
        var conv = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "text", Parts = ["System prompt"], Weight = 0.0 },
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Hello"], Weight = 1.0 },
                new StoredMessage { Id = "m2", Role = "assistant", ContentType = "text", Parts = ["Hi there"], Weight = 1.0 },
            ],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.DoesNotContain("System prompt", text);
        Assert.Contains("user: Hello", text);
        Assert.Contains("assistant: Hi there", text);
    }

    [Fact]
    public void ToEmbeddingText_ExcludesHiddenMessages()
    {
        var conv = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "user_editable_context", Parts = ["[User Profile] ..."], IsHidden = true },
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Real question"] },
                new StoredMessage { Id = "m2", Role = "assistant", ContentType = "text", Parts = ["Real answer"] },
            ],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.DoesNotContain("User Profile", text);
        Assert.Contains("user: Real question", text);
        Assert.Contains("assistant: Real answer", text);
    }

    [Fact]
    public void ToEmbeddingText_NullWeight_IncludesMessage()
    {
        var conv = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Hello"], Weight = null },
            ],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.Contains("user: Hello", text);
    }

    [Fact]
    public void ToEmbeddingText_AllMessagesHidden_StillIncludesTitleAndSummary()
    {
        var conv = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Topic",
            Summary = "A summary.",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "text", Parts = ["Hidden"], Weight = 0.0 },
                new StoredMessage { Id = "m1", Role = "system", ContentType = "text", Parts = ["Also hidden"], IsHidden = true },
            ],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.Contains("Topic", text);
        Assert.Contains("A summary.", text);
        Assert.DoesNotContain("Hidden", text);
        Assert.DoesNotContain("Also hidden", text);
    }

    [Fact]
    public void ToEmbeddingText_WithCitations_IncludesCitationContext()
    {
        var conv = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Research",
            LinearisedMessages =
            [
                new StoredMessage
                {
                    Id = "m1",
                    Role = "assistant",
                    ContentType = "text",
                    Parts = ["Here is the information."],
                    Citations =
                    [
                        new StoredCitation { Name = "Wikipedia: AI", Source = "https://en.wikipedia.org/wiki/AI" },
                        new StoredCitation { Name = null, Source = "https://example.com/article" },
                    ],
                },
            ],
        };

        var text = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.Contains("[Cited: Wikipedia: AI]", text);
        Assert.Contains("[Cited: https://example.com/article]", text);
    }

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var chunks = EmbeddingService.ChunkText("Hello world", 100);

        Assert.Single(chunks);
        Assert.Equal("Hello world", chunks[0]);
    }

    [Fact]
    public void ChunkText_LongText_SplitsOnNewlines()
    {
        var text = "Line one\nLine two\nLine three\nLine four\n";

        var chunks = EmbeddingService.ChunkText(text, 20);

        Assert.True(chunks.Count >= 2);
        // Each chunk should be within the limit.
        Assert.All(chunks, c => Assert.True(c.Length <= 20));
        // Reassembled chunks equal the original text.
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void ChunkText_NoNewlines_SplitsAtMaxChars()
    {
        var text = new string('x', 50);

        var chunks = EmbeddingService.ChunkText(text, 20);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(20, chunks[0].Length);
        Assert.Equal(20, chunks[1].Length);
        Assert.Equal(10, chunks[2].Length);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public async Task EmbedAsync_ContextLengthError_FallsBackToChunking()
    {
        // Build a conversation with enough content to exceed FallbackChunkChars.
        var longMessages = Enumerable.Range(0, 100)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = "user",
                ContentType = "text",
                Parts = [$"This is message number {i} with enough text to contribute to a long conversation."],
            })
            .ToList();

        var conv = new StoredConversation
        {
            ConversationId = "long1",
            Title = "Long Conversation",
            ProcessingStatus = ConversationProcessingStatus.Imported,
            LinearisedMessages = longMessages,
        };

        var repository = new FakeConversationRepository();
        repository.Seed([conv]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(values =>
        {
            callCount++;
            var input = values.First();
            // Reject the first (full-text) attempt, accept chunked attempts.
            if (input.Length > EmbeddingService.FallbackChunkChars)
                throw new InvalidOperationException("the input length exceeds the context length");
            return [new Embedding<float>(TestVector)];
        });

        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        // The first call failed, then multiple chunk calls succeeded.
        Assert.True(callCount > 1, "Expected multiple generator calls due to chunking fallback.");
        Assert.Single(qdrant.Upserted);
    }

    [Fact]
    public async Task EmbedAsync_NonContextError_StillMarksAsError()
    {
        // Errors that are NOT context-length related should NOT trigger chunking.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new ThrowingEmbeddingGenerator(new InvalidOperationException("Some other model error"));
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_TransientError_RetriesAndSucceeds()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(_ =>
        {
            callCount++;
            if (callCount <= 2)
                throw new HttpRequestException("Service temporarily unavailable");
            return [new Embedding<float>(TestVector)];
        });

        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, callCount); // 2 transient failures + 1 success
        Assert.Single(qdrant.Upserted);
    }

    [Fact]
    public async Task EmbedAsync_TransientErrorExhaustsRetries_MarksAsError()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        // Always throws a transient error — retries should be exhausted.
        var generator = new ThrowingEmbeddingGenerator(new HttpRequestException("Service unavailable"));
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.StatusUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_ContextLengthError_NotRetried_FallsToChunking()
    {
        // Context-length errors should NOT be retried — they should immediately
        // fall through to the chunking path.
        var repository = new FakeConversationRepository();
        var longMessages = Enumerable.Range(0, 100)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = "user",
                ContentType = "text",
                Parts = [$"This is message number {i} with enough text."],
            })
            .ToList();
        repository.Seed([new StoredConversation
        {
            ConversationId = "c1",
            Title = "Long",
            ProcessingStatus = ConversationProcessingStatus.Imported,
            LinearisedMessages = longMessages,
        }]);

        int fullTextAttempts = 0;
        int chunkAttempts = 0;
        var generator = new FakeEmbeddingGenerator(values =>
        {
            var input = values.First();
            if (input.Length > EmbeddingService.FallbackChunkChars)
            {
                fullTextAttempts++;
                throw new InvalidOperationException("the input length exceeds the context length");
            }
            chunkAttempts++;
            return [new Embedding<float>(TestVector)];
        });

        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, StandardRagOptions, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        // The full-text path should be attempted exactly once (no retries for context-length errors).
        Assert.Equal(1, fullTextAttempts);
        Assert.True(chunkAttempts > 0, "Expected chunk calls after context-length fallback.");
        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), true)] // no status => connection-level fault
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    public void IsTransientError_ClassifiesCorrectly(Type exceptionType, bool expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "test error")!;
        Assert.Equal(expected, EmbeddingService.IsTransientError(ex));
    }

    [Theory]
    // Permanent failures — an identical retry cannot fix these.
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    // Transient failures — a retry could plausibly succeed.
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    public void IsTransientError_HttpStatus_ClassifiesByRetryability(HttpStatusCode status, bool expected)
    {
        var ex = new HttpRequestException("http error", inner: null, statusCode: status);
        Assert.Equal(expected, EmbeddingService.IsTransientError(ex));
    }

    [Theory]
    // Provider SDK exceptions (OpenAI/Azure) carry the HTTP status on an int `Status` property
    // rather than deriving from HttpRequestException; they must classify the same way.
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public void IsTransientError_SdkExceptionWithStatus_ClassifiesByRetryability(int status, bool expected)
    {
        Assert.Equal(expected, EmbeddingService.IsTransientError(new FakeStatusException(status)));
    }

    /// <summary>
    /// Mimics a provider SDK exception (e.g. System.ClientModel's ClientResultException) that
    /// exposes the HTTP status on a public <c>int Status</c> property without deriving from
    /// <see cref="HttpRequestException"/>.
    /// </summary>
    public sealed class FakeStatusException(int status) : Exception("provider sdk error")
    {
        public int Status { get; } = status;
    }

    /// <summary>
    /// Mimics a provider SDK exception (e.g. System.ClientModel's ClientResultException) that
    /// exposes the server's error body via a parameterless <c>GetRawResponse()</c> whose result
    /// carries a <c>Content</c> property — the shape <see cref="EmbeddingService.TryGetServerErrorBody"/>
    /// reads reflectively.
    /// </summary>
    public sealed class FakeRawResponseException(int status, string body) : Exception("provider sdk error")
    {
        public int Status { get; } = status;

        public FakeRawResponse GetRawResponse() => new(body);

        public sealed class FakeRawResponse(string body)
        {
            private readonly string body = body;

            public override string ToString() => body;

            // The reflective reader invokes `Content` and calls ToString() on the result.
            public object Content => this;
        }
    }

    [Fact]
    public void TryGetServerErrorBody_ReadsRawResponseContent()
    {
        var ex = new FakeRawResponseException(400, """{"error":{"message":"model not found"}}""");

        Assert.True(EmbeddingService.TryGetServerErrorBody(ex, out var body));
        Assert.Contains("model not found", body);
    }

    [Fact]
    public void TryGetServerErrorBody_WalksInnerExceptions()
    {
        var inner = new FakeRawResponseException(400, "the real server error");
        var outer = new InvalidOperationException("wrapper", inner);

        Assert.True(EmbeddingService.TryGetServerErrorBody(outer, out var body));
        Assert.Equal("the real server error", body);
    }

    [Fact]
    public void TryGetServerErrorBody_TruncatesLongBodies()
    {
        var ex = new FakeRawResponseException(400, new string('x', 5_000));

        Assert.True(EmbeddingService.TryGetServerErrorBody(ex, out var body));
        Assert.True(body!.Length < 5_000);
        Assert.EndsWith("(truncated)", body);
    }

    [Fact]
    public void TryGetServerErrorBody_NoRawResponse_ReturnsFalse()
    {
        Assert.False(EmbeddingService.TryGetServerErrorBody(new InvalidOperationException("no response"), out var body));
        Assert.Null(body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    public void GetHttpStatusCode_ReadsHttpRequestException(HttpStatusCode status, int expected)
    {
        var ex = new HttpRequestException("http error", inner: null, statusCode: status);
        Assert.Equal(expected, EmbeddingService.GetHttpStatusCode(ex));
    }

    [Fact]
    public void GetHttpStatusCode_ReadsSdkStatusProperty()
    {
        Assert.Equal(400, EmbeddingService.GetHttpStatusCode(new FakeStatusException(400)));
    }

    [Fact]
    public void GetHttpStatusCode_WalksInnerExceptions()
    {
        var inner = new HttpRequestException("http error", inner: null, statusCode: HttpStatusCode.BadRequest);
        var outer = new InvalidOperationException("wrapper", inner);
        Assert.Equal(400, EmbeddingService.GetHttpStatusCode(outer));
    }

    [Fact]
    public void GetHttpStatusCode_NoStatus_ReturnsZero()
    {
        Assert.Equal(0, EmbeddingService.GetHttpStatusCode(new InvalidOperationException("no status")));
    }

    [Fact]
    public void IsTransientError_WrappedHttpRequestException_ReturnsTrue()
    {
        var inner = new HttpRequestException("Connection refused");
        var outer = new InvalidOperationException("Embedding failed", inner);
        Assert.True(EmbeddingService.IsTransientError(outer));
    }

    [Fact]
    public void IsTransientError_TaskCanceledException_WithDefaultToken_ReturnsTrue()
    {
        var ex = new TaskCanceledException("The request timed out");
        Assert.True(EmbeddingService.IsTransientError(ex));
    }

    [Fact]
    public void IsTransientError_TaskCanceledException_WithExplicitToken_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("Cancelled", null, cts.Token);
        Assert.False(EmbeddingService.IsTransientError(ex));
    }
    [Fact]
    public void ToEmbeddingText_RespectsMaxChars()
    {
        var conv = MakeLongConversation("long", 20_000);

        var standard = conv.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);
        var longContext = conv.ToEmbeddingText(EmbeddingService.LongContextMaxEmbeddingTextChars);

        Assert.True(standard.Length <= EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(longContext.Length > EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(longContext.Length <= EmbeddingService.LongContextMaxEmbeddingTextChars);
    }

    [Fact]
    public async Task EmbedAsync_WithReranking_EmbedsLongTextInSingleCall()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeLongConversation("long", 20_000)]);

        var inputs = new List<string>();
        var generator = new FakeEmbeddingGenerator(values =>
        {
            inputs.Add(values.First());
            return [new Embedding<float>(TestVector)];
        });

        var options = Options.Create(new RagOptions { UseReranking = true });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, options, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        var input = Assert.Single(inputs);
        Assert.True(input.Length > EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(input.Length <= EmbeddingService.LongContextMaxEmbeddingTextChars);
    }

    [Fact]
    public async Task EmbedAsync_WithReranking_ContextLengthError_RetriesTrimmedTextBeforeChunking()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeLongConversation("long", 20_000)]);

        var inputs = new List<string>();
        var generator = new FakeEmbeddingGenerator(values =>
        {
            var input = values.First();
            inputs.Add(input);
            // Model only accepts the standard length.
            if (input.Length > EmbeddingService.MaxEmbeddingTextChars)
                throw new InvalidOperationException("the input length exceeds the context length");
            return [new Embedding<float>(TestVector)];
        });

        var options = Options.Create(new RagOptions { UseReranking = true });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, options, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        // Full text rejected, then the trimmed text accepted — no chunking.
        Assert.Equal(2, inputs.Count);
        Assert.True(inputs[0].Length > EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(inputs[1].Length <= EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(inputs[1].Length > EmbeddingService.FallbackChunkChars);
    }

    [Fact]
    public async Task EmbedAsync_WithReranking_TrimmedTextRejected_ChunksTrimmedText()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeLongConversation("long", 20_000)]);

        var inputs = new List<string>();
        var generator = new FakeEmbeddingGenerator(values =>
        {
            var input = values.First();
            inputs.Add(input);
            if (input.Length > EmbeddingService.FallbackChunkChars)
                throw new InvalidOperationException("the input length exceeds the context length");
            return [new Embedding<float>(TestVector)];
        });

        var options = Options.Create(new RagOptions { UseReranking = true });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, options, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        // Full text, trimmed text, then chunks of the trimmed text only (not the full 20k).
        var chunks = inputs.Skip(2).ToList();
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Sum(c => c.Length) <= EmbeddingService.MaxEmbeddingTextChars);
    }

    [Fact]
    public async Task EmbedAsync_ExplicitMaxEmbeddingChars_OverridesRerankingDefault()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeLongConversation("long", 20_000)]);

        var inputs = new List<string>();
        var generator = new FakeEmbeddingGenerator(values =>
        {
            inputs.Add(values.First());
            return [new Embedding<float>(TestVector)];
        });

        var options = Options.Create(new RagOptions { UseReranking = true, MaxEmbeddingChars = 12_000 });
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), ZeroDelayTimeProvider.Instance, options, NullLogger<EmbeddingService>.Instance);

        await service.EmbedAsync();

        var input = Assert.Single(inputs);
        Assert.True(input.Length > EmbeddingService.MaxEmbeddingTextChars);
        Assert.True(input.Length <= 12_000);
    }
}
