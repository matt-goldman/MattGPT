using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IEmbeddingGenerator that returns a fixed embedding vector or throws on demand.
/// </summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Func<IEnumerable<string>, IList<Embedding<float>>> _handler;

    public FakeEmbeddingGenerator(float[] vector)
        : this(_ => [new Embedding<float>(vector)]) { }

    public FakeEmbeddingGenerator(Func<IEnumerable<string>, IList<Embedding<float>>> handler)
        => _handler = handler;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(_handler(values)));

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Fake IEmbeddingGenerator that always throws an exception.
/// </summary>
internal sealed class ThrowingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Exception _exception;

    public ThrowingEmbeddingGenerator(Exception exception) => _exception = exception;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw _exception;

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

public class EmbeddingServiceTests
{
    private static StoredConversation MakeSummarisedConversation(string id, string? summary = "A test summary.")
        => new()
        {
            ConversationId = id,
            Title = "Test",
            Summary = summary,
            ProcessingStatus = ConversationProcessingStatus.Summarised,
        };

    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    [Fact]
    public async Task EmbedAsync_UpdatesStatusToEmbedded()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeSummarisedConversation("c1")]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal("c1", repository.EmbeddingUpdates[0].Id);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.EmbeddingUpdates[0].Status);
        Assert.Equal(TestVector, repository.EmbeddingUpdates[0].Embedding);
    }

    [Fact]
    public async Task EmbedAsync_EmbeddingError_MarksAsEmbeddingError()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeSummarisedConversation("c1")]);

        var generator = new ThrowingEmbeddingGenerator(new InvalidOperationException("Model unavailable"));
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(1, result.Errors);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal(ConversationProcessingStatus.EmbeddingError, repository.EmbeddingUpdates[0].Status);
    }

    [Fact]
    public async Task EmbedAsync_NoSummary_MarksAsEmbeddedSkipped()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeSummarisedConversation("c1", summary: null)]);

        var generator = new FakeEmbeddingGenerator(TestVector);
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Single(repository.EmbeddingUpdates);
        Assert.Equal(ConversationProcessingStatus.Embedded, repository.EmbeddingUpdates[0].Status);
        Assert.Null(repository.EmbeddingUpdates[0].Embedding);
    }

    [Fact]
    public async Task EmbedAsync_MultipleConversations_AllProcessed()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeSummarisedConversation("c1"),
            MakeSummarisedConversation("c2"),
            MakeSummarisedConversation("c3"),
        ]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(_ =>
        {
            callCount++;
            return [new Embedding<float>(TestVector)];
        });
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(3, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task EmbedAsync_ErrorDoesNotAbortBatch()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeSummarisedConversation("c1"),
            MakeSummarisedConversation("c2"),
            MakeSummarisedConversation("c3"),
        ]);

        int callCount = 0;
        var generator = new FakeEmbeddingGenerator(_ =>
        {
            callCount++;
            if (callCount == 2)
                throw new InvalidOperationException("Embedding error on second call");
            return [new Embedding<float>(TestVector)];
        });
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

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
        var service = new EmbeddingService(repository, generator, NullLogger<EmbeddingService>.Instance);

        var result = await service.EmbedAsync();

        Assert.Equal(0, result.Embedded);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
    }
}
