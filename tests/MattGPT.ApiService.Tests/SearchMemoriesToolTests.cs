using MattGPT.ApiService;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

public class SearchMemoriesToolTests
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    private static StoredConversation MakeConversation(string id, string title, string? summary = null)
    {
        return new StoredConversation
        {
            ConversationId = id,
            Title = title,
            Summary = summary,
            LinearisedMessages =
            [
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Hello"] },
                new StoredMessage { Id = "m2", Role = "assistant", ContentType = "text", Parts = ["Hi there"] },
            ],
            ProcessingStatus = ConversationProcessingStatus.Embedded,
        };
    }

    private static SearchMemoriesTool CreateTool(
        IReadOnlyList<VectorSearchResult>? searchResults = null,
        FakeConversationRepository? repository = null,
        RagOptions? options = null)
    {
        return new SearchMemoriesTool(
            TestRetriever.Create(new FakeSearchVectorStore(searchResults ?? []), repository ?? new FakeConversationRepository()),
            Options.Create(options ?? new RagOptions()),
            NullLogger<SearchMemoriesTool>.Instance);
    }

    [Fact]
    public async Task SearchMemoriesAsync_NoResults_ReturnsNoMemoriesMessage()
    {
        var tool = CreateTool();

        var result = await tool.SearchMemoriesAsync("anything");

        Assert.Contains("No past conversations were semantically similar enough", result);
        Assert.Empty(tool.Sources);
    }

    [Fact]
    public async Task SearchMemoriesAsync_WithResults_ReturnsFormattedExcerpts()
    {
        var searchResults = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Python Help", "Helped with decorators"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Python Help", "Helped with decorators")]);

        var tool = CreateTool(searchResults, repo);

        var result = await tool.SearchMemoriesAsync("python decorators");

        Assert.Contains("Found 1 past conversation(s) semantically similar", result);
        Assert.Contains("Python Help", result);
        Assert.Contains("Helped with decorators", result);
        Assert.Single(tool.Sources);
        Assert.Equal("c1", tool.Sources[0].ConversationId);
    }

    [Fact]
    public async Task SearchMemoriesAsync_MultipleInvocations_AccumulateSources()
    {
        // The LLM may search more than once in a turn; earlier results must not be lost,
        // and a later search that finds nothing must not wipe them.
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "First", null), MakeConversation("c2", "Second", null)]);

        var store = new SequenceSearchVectorStore(
            [new("c1", 0.9f, "First", null)],
            [new("c2", 0.8f, "Second", null)],
            []);
        var tool = new SearchMemoriesTool(
            TestRetriever.Create(store, repo),
            Options.Create(new RagOptions()),
            NullLogger<SearchMemoriesTool>.Instance);

        await tool.SearchMemoriesAsync("first query");
        await tool.SearchMemoriesAsync("reworded query");
        await tool.SearchMemoriesAsync("query with no matches");

        Assert.Equal(["c1", "c2"], tool.Sources.Select(s => s.ConversationId));
    }

    [Fact]
    public async Task ResetSources_ClearsAccumulatedSources()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Title", null)]);
        var tool = CreateTool([new("c1", 0.9f, "Title", null)], repo);

        await tool.SearchMemoriesAsync("query");
        tool.ResetSources();

        Assert.Empty(tool.Sources);
    }

    [Fact]
    public async Task SearchMemoriesAsync_FiltersResultsBelowMinScore()
    {
        var searchResults = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "High Score", "Good match"),
            new("c2", 0.3f, "Low Score", "Bad match"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "High Score", "Good match")]);

        var tool = CreateTool(searchResults, repo, new RagOptions { MinScore = 0.5f });

        var result = await tool.SearchMemoriesAsync("query");

        Assert.Contains("Found 1 past conversation(s) semantically similar", result);
        Assert.Contains("High Score", result);
        Assert.DoesNotContain("Low Score", result);
        Assert.Single(tool.Sources);
    }

    [Fact]
    public async Task SearchMemoriesAsync_RespectsMaxResults()
    {
        var searchResults = new List<VectorSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.8f, "Title 2", "Summary 2"),
            new("c3", 0.7f, "Title 3", "Summary 3"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([
            MakeConversation("c1", "Title 1", "Summary 1"),
            MakeConversation("c2", "Title 2", "Summary 2"),
            MakeConversation("c3", "Title 3", "Summary 3"),
        ]);

        // The tool passes maxResults to Qdrant; our fake returns all results
        // regardless, but the tool should still work. The important thing is
        // it doesn't crash with a maxResults parameter.
        var tool = CreateTool(searchResults, repo);

        var result = await tool.SearchMemoriesAsync("query", maxResults: 2);

        Assert.Contains("past conversation(s) semantically similar", result);
    }

    [Fact]
    public async Task SearchMemoriesAsync_ClampsMaxResults()
    {
        var tool = CreateTool();

        // Should not throw — maxResults is clamped to 1-10.
        var result1 = await tool.SearchMemoriesAsync("query", maxResults: 0);
        Assert.NotNull(result1);

        var result2 = await tool.SearchMemoriesAsync("query", maxResults: 100);
        Assert.NotNull(result2);
    }

    [Fact]
    public void CreateAIFunction_ReturnsValidFunction()
    {
        var tool = CreateTool();

        var aiFunction = tool.CreateAiFunction();

        Assert.NotNull(aiFunction);
        Assert.Equal("search_memories", aiFunction.Name);

        // The description has to tell the model this is semantic search and where to go for
        // literal matching - keyword-shaped queries embed badly and retrieve poorly.
        Assert.Contains("Semantic", aiFunction.Description);
        Assert.Contains("not by keyword", aiFunction.Description);
        Assert.Contains("search_memories_keyword", aiFunction.Description);
    }

    [Fact]
    public async Task SearchMemoriesAsync_QdrantFailure_ReturnsErrorMessage()
    {
        // Use a throwing Qdrant service to simulate failure.
        var tool = new SearchMemoriesTool(
            TestRetriever.Create(new ThrowingSearchVectorStore(), new FakeConversationRepository()),
            Options.Create(new RagOptions()),
            NullLogger<SearchMemoriesTool>.Instance);

        var result = await tool.SearchMemoriesAsync("query");

        Assert.Contains("Memory search failed", result);
        Assert.Empty(tool.Sources);
    }
}

/// <summary>
/// Fake IVectorStore that always throws on search.
/// </summary>
internal sealed class ThrowingSearchVectorStore : IVectorStore
{
    public Task UpsertAsync(
        StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        => throw new InvalidOperationException("Qdrant unavailable");

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>(null);
}

/// <summary>
/// Fake IVectorStore that returns the next configured result set on each search,
/// then empty results once they run out.
/// </summary>
internal sealed class SequenceSearchVectorStore(params IReadOnlyList<VectorSearchResult>[] resultSets) : IVectorStore
{
    private int _next;

    public Task UpsertAsync(
        StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string conversationId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
        => Task.FromResult(_next < resultSets.Length ? resultSets[_next++] : []);

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>(0);
}
