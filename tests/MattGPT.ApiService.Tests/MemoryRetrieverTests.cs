using MattGPT.ApiService.Services;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Builds a <see cref="MemoryRetriever"/> over test fakes.
/// </summary>
internal static class TestRetriever
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    public static MemoryRetriever Create(
        IVectorStore vectorStore,
        IConversationRepository? repository = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
        => new(
            embeddingGenerator ?? new FakeEmbeddingGenerator(TestVector),
            vectorStore,
            repository ?? new FakeConversationRepository(),
            new NullCurrentUserService(),
            NullLogger<MemoryRetriever>.Instance);
}

public class MemoryRetrieverTests
{
    private static StoredConversation MakeConversation(string id) => new()
    {
        ConversationId = id,
        Title = id,
        ProcessingStatus = ConversationProcessingStatus.Embedded,
        LinearisedMessages = [new StoredMessage { Id = "m0", Role = "user", ContentType = "text", Parts = [$"Content of {id}"] }],
    };

    [Fact]
    public async Task RetrieveAsync_FiltersBelowMinScore_AndLoadsConversations()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1"), MakeConversation("c2")]);

        var store = new FakeSearchVectorStore(
        [
            new VectorSearchResult("c1", 0.9f, "C1", null),
            new VectorSearchResult("c2", 0.3f, "C2", null),
        ]);

        var result = await TestRetriever.Create(store, repo).RetrieveAsync("query", limit: 5, minScore: 0.5f);

        var hit = Assert.Single(result.Results);
        Assert.Equal("c1", hit.ConversationId);
        Assert.True(result.Conversations.ContainsKey("c1"));
        Assert.False(result.Conversations.ContainsKey("c2"));
    }

    [Fact]
    public async Task RetrieveAsync_NoHitsAboveThreshold_ReturnsEmptyWithoutFetching()
    {
        var store = new FakeSearchVectorStore([new VectorSearchResult("c1", 0.2f, "C1", null)]);

        var result = await TestRetriever.Create(store).RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Empty(result.Results);
        Assert.Empty(result.Conversations);
    }

    [Fact]
    public async Task RetrieveAsync_MissingConversation_KeepsHitWithoutLookupEntry()
    {
        // The vector store can reference a conversation the repository no longer returns.
        var store = new FakeSearchVectorStore([new VectorSearchResult("gone", 0.9f, "Gone", null)]);

        var result = await TestRetriever.Create(store).RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Single(result.Results);
        Assert.Empty(result.Conversations);
    }

    [Fact]
    public async Task RetrieveAsync_PassesLimitToVectorStore()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store).RetrieveAsync("query", limit: 7, minScore: 0.5f);

        Assert.Equal(7, store.LastLimit);
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public int? LastLimit { get; private set; }

        public Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        {
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
            => Task.FromResult<ulong?>(0);
    }
}
