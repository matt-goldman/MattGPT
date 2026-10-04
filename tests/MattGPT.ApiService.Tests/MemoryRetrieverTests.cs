using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        RagOptions? options = null,
        IReranker? reranker = null)
        => new(
            embeddingGenerator ?? new FakeEmbeddingGenerator(TestVector),
            vectorStore,
            repository ?? new FakeConversationRepository(),
            new NullCurrentUserService(),
            Options.Create(options ?? new RagOptions()),
            NullLogger<MemoryRetriever>.Instance,
            reranker);
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

    // ── Reranking ──

    private static readonly RagOptions RerankingOptions = new() { UseReranking = true, RerankCandidateCount = 30 };

    private static (FakeConversationRepository Repo, FakeSearchVectorStore Store) ThreeCandidates()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1"), MakeConversation("c2"), MakeConversation("c3")]);
        var store = new FakeSearchVectorStore(
        [
            new VectorSearchResult("c1", 0.9f, "C1", null),
            new VectorSearchResult("c2", 0.6f, "C2", null),
            new VectorSearchResult("c3", 0.2f, "C3", null), // below any sensible MinScore
        ]);
        return (repo, store);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_OrdersByRerankerAndUsesItsScores()
    {
        var (repo, store) = ThreeCandidates();
        // The reranker prefers c3, which vector search ranked last and below MinScore.
        var reranker = new ScriptedReranker((_, _, _) => [new RerankResult(2, 0.95f), new RerankResult(0, 0.40f)]);

        var result = await TestRetriever.Create(store, repo, options: RerankingOptions, reranker: reranker)
            .RetrieveAsync("query", limit: 2, minScore: 0.5f);

        Assert.Equal(["c3", "c1"], result.Results.Select(r => r.ConversationId));
        Assert.Equal([0.95f, 0.40f], result.Results.Select(r => r.Score));
        Assert.Equal(["c1", "c3"], result.Conversations.Keys.Order());
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_SendsCandidateTextAndRequestedLimit()
    {
        var (repo, store) = ThreeCandidates();
        var reranker = new ScriptedReranker((_, _, _) => []);

        await TestRetriever.Create(store, repo, options: RerankingOptions, reranker: reranker)
            .RetrieveAsync("the query", limit: 2, minScore: 0.5f);

        Assert.Equal("the query", reranker.LastQuery);
        Assert.Equal(2, reranker.LastTopN);
        // Every candidate is sent, including the one below MinScore, as conversation text.
        Assert.Equal(3, reranker.LastDocuments!.Count);
        Assert.Contains("Content of c3", reranker.LastDocuments[2]);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_CapsDocumentLength()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([new StoredConversation
        {
            ConversationId = "long",
            Title = "Long",
            ProcessingStatus = ConversationProcessingStatus.Embedded,
            LinearisedMessages = [new StoredMessage { Id = "m0", Role = "user", ContentType = "text", Parts = [new string('x', 10_000)] }],
        }]);
        var store = new FakeSearchVectorStore([new VectorSearchResult("long", 0.9f, "Long", null)]);
        var reranker = new ScriptedReranker((_, _, _) => []);

        await TestRetriever.Create(store, repo,
                options: new RagOptions { UseReranking = true, RerankDocumentChars = 500 }, reranker: reranker)
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.True(reranker.LastDocuments![0].Length <= 500);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_RequestsCandidateCountFromVectorStore()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store, options: new RagOptions { UseReranking = true, RerankCandidateCount = 25 },
                reranker: new ScriptedReranker((_, _, _) => []))
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Equal(25, store.LastLimit);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_LimitAboveCandidateCount_RequestsLimit()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store, options: new RagOptions { UseReranking = true, RerankCandidateCount = 3 },
                reranker: new ScriptedReranker((_, _, _) => []))
            .RetrieveAsync("query", limit: 10, minScore: 0.5f);

        Assert.Equal(10, store.LastLimit);
    }

    [Fact]
    public async Task RetrieveAsync_RerankerFails_FallsBackToVectorOrderWithMinScore()
    {
        var (repo, store) = ThreeCandidates();
        var reranker = new ScriptedReranker((_, _, _) => throw new HttpRequestException("rerank service down"));

        var result = await TestRetriever.Create(store, repo, options: RerankingOptions, reranker: reranker)
            .RetrieveAsync("query", limit: 2, minScore: 0.5f);

        Assert.Equal(["c1", "c2"], result.Results.Select(r => r.ConversationId));
        Assert.Equal([0.9f, 0.6f], result.Results.Select(r => r.Score));
        Assert.Equal(["c1", "c2"], result.Conversations.Keys.Order());
    }

    [Fact]
    public async Task RetrieveAsync_RerankingEnabledWithoutReranker_UsesVectorSearch()
    {
        var (repo, store) = ThreeCandidates();

        var result = await TestRetriever.Create(store, repo, options: RerankingOptions)
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Equal(["c1", "c2"], result.Results.Select(r => r.ConversationId));
    }

    [Fact]
    public async Task RetrieveAsync_RerankerRegisteredButDisabled_DoesNotRerank()
    {
        var (repo, store) = ThreeCandidates();
        var reranker = new ScriptedReranker((_, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await TestRetriever.Create(store, repo, options: new RagOptions { UseReranking = false }, reranker: reranker)
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Null(reranker.LastQuery);
        Assert.Equal(["c1", "c2"], result.Results.Select(r => r.ConversationId));
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_MissingConversation_SendsTitleAndSummary()
    {
        // The vector store can reference a conversation the repository no longer returns.
        var store = new FakeSearchVectorStore([new VectorSearchResult("gone", 0.9f, "Gone Title", "Gone summary")]);
        var reranker = new ScriptedReranker((_, _, _) => [new RerankResult(0, 0.8f)]);

        var result = await TestRetriever.Create(store, options: RerankingOptions, reranker: reranker)
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.Contains("Gone Title", reranker.LastDocuments![0]);
        Assert.Contains("Gone summary", reranker.LastDocuments[0]);
        Assert.Single(result.Results);
        Assert.Empty(result.Conversations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MemoryRetriever_ResolvesFromContainer_WithAndWithoutReranker(bool registerReranker)
    {
        // MemoryRetriever's reranker parameter is optional: the container must supply the registered
        // HTTP reranker when reranking is on, and fall back to null (vector-only) when it isn't registered.
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<RagOptions>(o => o.UseReranking = registerReranker);
        services.Configure<LlmOptions>(o => o.RerankingModelId = "m");
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator([0.1f]));
        services.AddSingleton<IVectorStore>(new FakeSearchVectorStore([]));
        services.AddSingleton<IConversationRepository>(new FakeConversationRepository());
        services.AddSingleton<ICurrentUserService>(new NullCurrentUserService());
        services.AddScoped<MemoryRetriever>();
        if (registerReranker)
            services.AddHttpClient<IReranker, CohereCompatibleReranker>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var retriever = scope.ServiceProvider.GetRequiredService<MemoryRetriever>();
        var result = await retriever.RetrieveAsync("query", 5, 0.5f);

        Assert.Empty(result.Results);
        Assert.Equal(registerReranker, scope.ServiceProvider.GetService<IReranker>() is CohereCompatibleReranker);
    }

    private sealed class ScriptedReranker(
        Func<string, IReadOnlyList<string>, int, IReadOnlyList<RerankResult>> handler) : IReranker
    {
        public string? LastQuery { get; private set; }
        public IReadOnlyList<string>? LastDocuments { get; private set; }
        public int? LastTopN { get; private set; }

        public Task<IReadOnlyList<RerankResult>> RerankAsync(
            string query, IReadOnlyList<string> documents, int topN, CancellationToken ct = default)
        {
            LastQuery = query;
            LastDocuments = documents;
            LastTopN = topN;
            return Task.FromResult(handler(query, documents, topN));
        }
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
