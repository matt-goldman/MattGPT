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
            TestChunker.For(options),
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
    public async Task RetrieveAsync_AsksTheStoreForSeveralChunksPerConversationWanted()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store, options: new RagOptions { ChunkCandidateMultiplier = 4 })
            .RetrieveAsync("query", limit: 7, minScore: 0.5f);

        // The store's limit counts chunks, so a conversation whose chunks all match cannot crowd the
        // others out of the candidate set.
        Assert.Equal(28, store.LastLimit);
    }

    [Fact]
    public async Task RetrieveAsync_RequestedLimitBelowRetainedBreadth_AsksForBreadth()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store,
                options: new RagOptions { RetainedConversations = 6, ChunkCandidateMultiplier = 2 })
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.Equal(12, store.LastLimit);
    }

    // ── Pooling, breadth and neighbour expansion ──

    /// <summary>A conversation of alternating turns, each message naming its index.</summary>
    private static StoredConversation MakeConversation(string id, int messageCount) => new()
    {
        ConversationId = id,
        Title = id,
        ProcessingStatus = ConversationProcessingStatus.Embedded,
        LinearisedMessages = [.. Enumerable.Range(0, messageCount).Select(i => new StoredMessage
        {
            Id = $"m{i}",
            Role = i % 2 == 0 ? "user" : "assistant",
            ContentType = "text",
            Parts = [$"{id} message {i}"],
        })],
    };

    /// <summary>A chunk-level hit, as a store holding chunk vectors returns one.</summary>
    private static VectorSearchResult ChunkHit(string conversationId, float score, int ordinal, int chunkCount = 6)
        => new(conversationId, score, conversationId, null,
            new ChunkHit(ordinal, chunkCount, ordinal, ordinal, ChunkingStrategy.Message));

    private static RagOptions MessageChunking(
        int retained = 4, float relative = 0.6f, int window = 1) => new()
    {
        ChunkingStrategy = ChunkingStrategy.Message,
        RetainedConversations = retained,
        RetainedRelativeScore = relative,
        NeighbourWindow = window,
    };

    [Fact]
    public async Task RetrieveAsync_PoolsChunkScoresToTheConversationByMaximum()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6), MakeConversation("c2", 6)]);

        // c1 matches weakly three times (sum 1.27); c2 matches strongly once. Max pooling puts c2 first;
        // sum pooling would put c1 first, which is the length bias this avoids.
        var store = new FakeSearchVectorStore(
        [
            new VectorSearchResult("c2", 0.80f, "c2", null, new ChunkHit(0, 6, 0, 0, ChunkingStrategy.Message)),
            ChunkHit("c1", 0.45f, 1),
            ChunkHit("c1", 0.42f, 2),
            ChunkHit("c1", 0.40f, 3),
        ]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(relative: 0f))
            .RetrieveAsync("query", limit: 5, minScore: 0.3f);

        Assert.Equal(["c2", "c1"], result.Results.Select(r => r.ConversationId));
        Assert.Equal([0.80f, 0.45f], result.Results.Select(r => r.Score));
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsOneResultPerConversation_NotPerChunk()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6)]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 0), ChunkHit("c1", 0.8f, 2), ChunkHit("c1", 0.7f, 4)]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking())
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        var hit = Assert.Single(result.Results);
        Assert.Equal(0, hit.Chunk!.Ordinal); // the best-matching chunk
    }

    [Fact]
    public async Task RetrieveAsync_RetainsSeveralConversations_EvenWhenTheCallerAsksForOne()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([.. new[] { "c1", "c2", "c3", "c4" }.Select(id => MakeConversation(id, 2))]);

        var store = new FakeSearchVectorStore(
        [
            ChunkHit("c1", 0.90f, 0), ChunkHit("c2", 0.85f, 0),
            ChunkHit("c3", 0.80f, 0), ChunkHit("c4", 0.75f, 0),
        ]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(retained: 4))
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.Equal(["c1", "c2", "c3", "c4"], result.Results.Select(r => r.ConversationId));
    }

    [Fact]
    public async Task RetrieveAsync_DropsConversationsBelowTheRelativeScoreFloor()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("dominant", 2), MakeConversation("weak", 2)]);

        // 0.5 clears MinScore but not 60% of the dominant conversation's 0.9, so breadth does not pad
        // the context with it.
        var store = new FakeSearchVectorStore([ChunkHit("dominant", 0.90f, 0), ChunkHit("weak", 0.50f, 0)]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(relative: 0.6f))
            .RetrieveAsync("query", limit: 5, minScore: 0.4f);

        Assert.Equal(["dominant"], result.Results.Select(r => r.ConversationId));
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsMatchingChunksExpandedToTheirNeighbours()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6)]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 2)]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(window: 1))
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        var chunks = result.MatchingChunks["c1"];
        Assert.Equal([1, 2, 3], chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public async Task RetrieveAsync_TwoNearbyMatches_ContributeEachChunkOnce()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6)]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 2), ChunkHit("c1", 0.8f, 3)]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(window: 1))
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Equal([1, 2, 3, 4], result.MatchingChunks["c1"].Select(c => c.Ordinal));
    }

    [Fact]
    public async Task RetrieveAsync_WithNeighbourExpansionDisabled_ReturnsOnlyTheMatchedChunks()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6)]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 2)]);

        var result = await TestRetriever.Create(store, repo, options: MessageChunking(window: 0))
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Equal([2], result.MatchingChunks["c1"].Select(c => c.Ordinal));
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
        var reranker = new ScriptedReranker((_, _, _) => [new RerankResult(2, 0.95f), new RerankResult(0, 0.80f)]);

        var result = await TestRetriever.Create(store, repo, options: RerankingOptions, reranker: reranker)
            .RetrieveAsync("query", limit: 2, minScore: 0.5f);

        Assert.Equal(["c3", "c1"], result.Results.Select(r => r.ConversationId));
        Assert.Equal([0.95f, 0.80f], result.Results.Select(r => r.Score));
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
        // Retained breadth, not the caller's top-k: a small limit must not narrow the context below it.
        Assert.Equal(4, reranker.LastTopN);
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

        await TestRetriever.Create(store,
                options: new RagOptions
                {
                    UseReranking = true,
                    RerankCandidateCount = 25,
                    ChunkCandidateMultiplier = 2,
                },
                reranker: new ScriptedReranker((_, _, _) => []))
            .RetrieveAsync("query", limit: 5, minScore: 0.5f);

        Assert.Equal(50, store.LastLimit);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_LimitAboveCandidateCount_RequestsLimit()
    {
        var store = new RecordingVectorStore();

        await TestRetriever.Create(store,
                options: new RagOptions
                {
                    UseReranking = true,
                    RerankCandidateCount = 3,
                    ChunkCandidateMultiplier = 1,
                },
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

    [Fact]
    public async Task RetrieveAsync_WithReranking_PrefersTheRecordAsTheCandidateDocument()
    {
        var conversation = MakeConversation("c1", 4);
        conversation.Summary = "A digest.";
        conversation.Record = "The record of what was decided.";
        var repo = new FakeConversationRepository();
        repo.Seed([conversation]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 0, chunkCount: 4)]);
        var reranker = new ScriptedReranker((_, _, _) => []);

        await TestRetriever.Create(store, repo,
                options: new RagOptions { UseReranking = true, ChunkingStrategy = ChunkingStrategy.Message },
                reranker: reranker)
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.Contains("The record of what was decided.", reranker.LastDocuments![0]);
        Assert.DoesNotContain("A digest.", reranker.LastDocuments[0]);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_NoRecord_UsesTheDigest()
    {
        var conversation = MakeConversation("c1", 4);
        conversation.Summary = "A digest.";
        var repo = new FakeConversationRepository();
        repo.Seed([conversation]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 0, chunkCount: 4)]);
        var reranker = new ScriptedReranker((_, _, _) => []);

        await TestRetriever.Create(store, repo,
                options: new RagOptions { UseReranking = true, ChunkingStrategy = ChunkingStrategy.Message },
                reranker: reranker)
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        Assert.Contains("A digest.", reranker.LastDocuments![0]);
        Assert.Contains("Title: c1", reranker.LastDocuments[0]);
    }

    [Fact]
    public async Task RetrieveAsync_WithReranking_NoRecordOrDigest_StitchesTheMatchingChunks()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", 6)]);

        var store = new FakeSearchVectorStore([ChunkHit("c1", 0.9f, 3)]);
        var reranker = new ScriptedReranker((_, _, _) => []);

        await TestRetriever.Create(store, repo,
                options: new RagOptions { UseReranking = true, ChunkingStrategy = ChunkingStrategy.Message },
                reranker: reranker)
            .RetrieveAsync("query", limit: 1, minScore: 0.5f);

        // The chunk that matched, not the opening of the conversation, and not its neighbours: this
        // document decides relevance.
        Assert.Contains("c1 message 3", reranker.LastDocuments![0]);
        Assert.DoesNotContain("c1 message 0", reranker.LastDocuments[0]);
        Assert.DoesNotContain("c1 message 2", reranker.LastDocuments[0]);
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
        services.AddSingleton<ConversationChunker>();
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

        public Task UpsertAsync(
            StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;

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
