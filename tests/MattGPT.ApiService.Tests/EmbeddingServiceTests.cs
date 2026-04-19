using MattGPT.ApiService.Services;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

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
    public List<(StoredConversation Conversation, float[] Vector)> Upserted { get; } = new();

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

    [Fact]
    public async Task EmbedAsync_ImportedConversation_UpdatesStatusToEmbedded()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal("c1", repository.EmbeddingUpdates[0].Id);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.EmbeddingUpdates[0].Status);
        Assert.Equal(TestVector, repository.EmbeddingUpdates[0].Embedding);
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
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.EmbeddingUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_NoContentAtAll_MarksAsEmbeddedSkipped()
    {
        // A conversation with no title, no summary, and no messages has nothing to embed.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", title: null, summary: null, messageCount: 0)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.EmbeddingUpdates[0].Status);
        Assert.Null(repository.EmbeddingUpdates[0].Embedding);
    }

    [Fact]
    public async Task EmbedAsync_NoSummaryButHasMessages_StillEmbeds()
    {
        // Conversations without a summary should still be embedded from their message content.
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", summary: null, messageCount: 3)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var qdrant = new FakeVectorStore();
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(2, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(3, callCount);
        Assert.Equal(3, repository.EmbeddingUpdates.Count);
    }

    [Fact]
    public async Task EmbedAsync_EmptyRepository_ReturnsZeroCounts()
    {
        var repository = new FakeConversationRepository();
        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

        await service.EmbedAsync();

        Assert.Single(qdrant.Upserted);
        Assert.Equal("c1", qdrant.Upserted[0].Conversation.ConversationId);
        Assert.Equal(TestVector, qdrant.Upserted[0].Vector);
    }

    [Fact]
    public async Task EmbedAsync_QdrantFails_StillMarksEmbedded()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, new ThrowingVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        // The MongoDB embedding should still be stored even if Qdrant fails.
        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.EmbeddingUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_EmptyConversation_DoesNotUpsertToQdrant()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", title: null, summary: null, messageCount: 0)]);

        var qdrant = new FakeVectorStore();
        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

        await service.EmbedAsync();

        // No embeddable content means nothing to upsert to Qdrant.
        Assert.Empty(qdrant.Upserted);
    }

    [Fact]
    public void BuildEmbeddingText_IncludesTitleAndMessages()
    {
        var conv = MakeConversation("c1", title: "My Topic", messageCount: 2);

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.Contains("My Topic", text);
        Assert.Contains("user: Message content 0", text);
        Assert.Contains("assistant: Message content 1", text);
    }

    [Fact]
    public void BuildEmbeddingText_WithSummary_IncludesSummary()
    {
        var conv = MakeConversation("c1", title: "My Topic", summary: "This is a summary.", messageCount: 1);

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.Contains("This is a summary.", text);
        Assert.Contains("My Topic", text);
    }

    [Fact]
    public void BuildEmbeddingText_TruncatesLongConversations()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.True(text.Length <= EmbeddingService.MaxEmbeddingTextChars);
    }

    [Fact]
    public void BuildEmbeddingText_ExcludesZeroWeightMessages()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.DoesNotContain("System prompt", text);
        Assert.Contains("user: Hello", text);
        Assert.Contains("assistant: Hi there", text);
    }

    [Fact]
    public void BuildEmbeddingText_ExcludesHiddenMessages()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.DoesNotContain("User Profile", text);
        Assert.Contains("user: Real question", text);
        Assert.Contains("assistant: Real answer", text);
    }

    [Fact]
    public void BuildEmbeddingText_NullWeight_IncludesMessage()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.Contains("user: Hello", text);
    }

    [Fact]
    public void BuildEmbeddingText_AllMessagesHidden_StillIncludesTitleAndSummary()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

        Assert.Contains("Topic", text);
        Assert.Contains("A summary.", text);
        Assert.DoesNotContain("Hidden", text);
        Assert.DoesNotContain("Also hidden", text);
    }

    [Fact]
    public void BuildEmbeddingText_WithCitations_IncludesCitationContext()
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

        var text = EmbeddingService.BuildEmbeddingText(conv);

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
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.EmbeddingUpdates[0].Status);
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
        var service = new EmbeddingService(repository, generator, qdrant, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.True(callCount > 2, "Expected retries before success.");
        Assert.Single(qdrant.Upserted);
    }

    [Fact]
    public async Task EmbedAsync_TransientErrorExhaustsRetries_MarksAsError()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        // Always throws a transient error — retries should be exhausted.
        var generator = new ThrowingEmbeddingGenerator(new HttpRequestException("Service unavailable"));
        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.EmbeddingUpdates[0].Status);
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

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(values =>
        {
            callCount++;
            var input = values.First();
            if (input.Length > EmbeddingService.FallbackChunkChars)
                throw new InvalidOperationException("the input length exceeds the context length");
            return [new Embedding<float>(TestVector)];
        });

        var service = new EmbeddingService(repository, generator, new FakeVectorStore(), NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        // Should have succeeded via chunking — the context-length error should NOT
        // have been retried (only 1 call for the full text, then chunk calls).
        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    public void IsTransientError_ClassifiesCorrectly(Type exceptionType, bool expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "test error")!;
        Assert.Equal(expected, EmbeddingService.IsTransientError(ex));
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
}
