using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
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
}
