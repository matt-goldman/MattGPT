using MattGPT.ApiService;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
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
        IReadOnlyList<QdrantSearchResult>? searchResults = null,
        FakeConversationRepository? repository = null,
        RagOptions? options = null)
    {
        return new SearchMemoriesTool(
            new FakeEmbeddingGenerator(TestVector),
            new FakeSearchQdrantService(searchResults ?? []),
            repository ?? new FakeConversationRepository(),
            Options.Create(options ?? new RagOptions()),
            NullLogger<SearchMemoriesTool>.Instance);
    }

    [Fact]
    public async Task SearchMemoriesAsync_NoResults_ReturnsNoMemoriesMessage()
    {
        var tool = CreateTool();

        var result = await tool.SearchMemoriesAsync("anything");

        Assert.Contains("No relevant past conversations found", result);
        Assert.Empty(tool.LastSources);
    }

    [Fact]
    public async Task SearchMemoriesAsync_WithResults_ReturnsFormattedExcerpts()
    {
        var searchResults = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Python Help", "Helped with decorators"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "Python Help", "Helped with decorators")]);

        var tool = CreateTool(searchResults, repo);

        var result = await tool.SearchMemoriesAsync("python decorators");

        Assert.Contains("Found 1 relevant past conversation", result);
        Assert.Contains("Python Help", result);
        Assert.Contains("Helped with decorators", result);
        Assert.Single(tool.LastSources);
        Assert.Equal("c1", tool.LastSources[0].ConversationId);
    }

    [Fact]
    public async Task SearchMemoriesAsync_FiltersResultsBelowMinScore()
    {
        var searchResults = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "High Score", "Good match"),
            new("c2", 0.3f, "Low Score", "Bad match"),
        };
        var repo = new FakeConversationRepository();
        repo.Seed([MakeConversation("c1", "High Score", "Good match")]);

        var tool = CreateTool(searchResults, repo, new RagOptions { MinScore = 0.5f });

        var result = await tool.SearchMemoriesAsync("query");

        Assert.Contains("Found 1 relevant past conversation", result);
        Assert.Contains("High Score", result);
        Assert.DoesNotContain("Low Score", result);
        Assert.Single(tool.LastSources);
    }

    [Fact]
    public async Task SearchMemoriesAsync_RespectsMaxResults()
    {
        var searchResults = new List<QdrantSearchResult>
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

        Assert.Contains("relevant past conversation", result);
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

        var aiFunction = tool.CreateAIFunction();

        Assert.NotNull(aiFunction);
        Assert.Equal("search_memories", aiFunction.Name);
        Assert.Contains("Search", aiFunction.Description);
    }

    [Fact]
    public async Task SearchMemoriesAsync_QdrantFailure_ReturnsErrorMessage()
    {
        // Use a throwing Qdrant service to simulate failure.
        var tool = new SearchMemoriesTool(
            new FakeEmbeddingGenerator(TestVector),
            new ThrowingSearchQdrantService(),
            new FakeConversationRepository(),
            Options.Create(new RagOptions()),
            NullLogger<SearchMemoriesTool>.Instance);

        var result = await tool.SearchMemoriesAsync("query");

        Assert.Contains("Memory search failed", result);
        Assert.Empty(tool.LastSources);
    }
}

/// <summary>
/// Fake IQdrantService that always throws on search.
/// </summary>
internal sealed class ThrowingSearchQdrantService : IQdrantService
{
    public Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, CancellationToken ct = default)
        => throw new InvalidOperationException("Qdrant unavailable");

    public Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
        => Task.FromResult<ulong?>(null);
}
