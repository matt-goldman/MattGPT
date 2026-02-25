using MattGPT.ApiService;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IQdrantService that returns configured search results.
/// </summary>
internal sealed class FakeSearchQdrantService : IQdrantService
{
    private readonly IReadOnlyList<QdrantSearchResult> _results;

    public FakeSearchQdrantService(IReadOnlyList<QdrantSearchResult> results)
        => _results = results;

    public Task UpsertAsync(MattGPT.ApiService.Models.StoredConversation conversation, float[] vector, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<QdrantSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, CancellationToken ct = default)
        => Task.FromResult(_results);
}

public class RagServiceTests
{
    private static readonly float[] TestVector = [0.1f, 0.2f, 0.3f];

    private static RagService CreateService(
        IReadOnlyList<QdrantSearchResult> searchResults,
        string llmResponse = "Test answer.",
        RagOptions? ragOptions = null)
    {
        var options = Options.Create(ragOptions ?? new RagOptions { TopK = 5, MinScore = 0.5f });
        return new RagService(
            new FakeEmbeddingGenerator(TestVector),
            new FakeSearchQdrantService(searchResults),
            new FakeChatClient(llmResponse),
            options,
            NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task ChatAsync_ReturnsLlmAnswer()
    {
        var service = CreateService([], llmResponse: "Hello from LLM.");

        var result = await service.ChatAsync("What did we discuss?");

        Assert.Equal("Hello from LLM.", result.Answer);
    }

    [Fact]
    public async Task ChatAsync_NoResults_ReturnsEmptySources()
    {
        var service = CreateService([]);

        var result = await service.ChatAsync("Any question");

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task ChatAsync_ResultsAboveThreshold_IncludedInSources()
    {
        var results = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.7f, "Title 2", "Summary 2"),
        };
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f });

        var result = await service.ChatAsync("query");

        Assert.Equal(2, result.Sources.Count);
        Assert.Contains(result.Sources, s => s.ConversationId == "c1");
        Assert.Contains(result.Sources, s => s.ConversationId == "c2");
    }

    [Fact]
    public async Task ChatAsync_ResultsBelowThreshold_ExcludedFromSources()
    {
        var results = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Title 1", "Summary 1"),
            new("c2", 0.3f, "Title 2", "Summary 2"), // below 0.5 threshold
        };
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f });

        var result = await service.ChatAsync("query");

        Assert.Single(result.Sources);
        Assert.Equal("c1", result.Sources[0].ConversationId);
    }

    [Fact]
    public async Task ChatAsync_AllResultsBelowThreshold_ReturnsEmptySources()
    {
        var results = new List<QdrantSearchResult>
        {
            new("c1", 0.2f, "Title 1", "Summary 1"),
            new("c2", 0.1f, "Title 2", "Summary 2"),
        };
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f });

        var result = await service.ChatAsync("query");

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task ChatAsync_SourcesContainCorrectMetadata()
    {
        var results = new List<QdrantSearchResult>
        {
            new("conv-123", 0.85f, "My Title", "My Summary"),
        };
        var service = CreateService(results, ragOptions: new RagOptions { TopK = 5, MinScore = 0.5f });

        var result = await service.ChatAsync("query");

        var source = Assert.Single(result.Sources);
        Assert.Equal("conv-123", source.ConversationId);
        Assert.Equal("My Title", source.Title);
        Assert.Equal("My Summary", source.Summary);
        Assert.Equal(0.85f, source.Score);
    }

    [Fact]
    public void BuildPrompt_WithContext_IncludesRetrievedConversations()
    {
        var context = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Test Title", "Test Summary"),
        };

        var prompt = RagService.BuildPrompt("What is this?", context);

        Assert.Contains("Test Title", prompt);
        Assert.Contains("Test Summary", prompt);
        Assert.Contains("What is this?", prompt);
    }

    [Fact]
    public void BuildPrompt_WithoutContext_ContainsNoContextMessage()
    {
        var prompt = RagService.BuildPrompt("Any query", []);

        Assert.Contains("No relevant past conversations were found", prompt);
        Assert.Contains("Any query", prompt);
    }

    [Fact]
    public void BuildPrompt_MultipleContextItems_AllIncluded()
    {
        var context = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Title A", "Summary A"),
            new("c2", 0.8f, "Title B", "Summary B"),
            new("c3", 0.7f, "Title C", "Summary C"),
        };

        var prompt = RagService.BuildPrompt("query", context);

        Assert.Contains("Title A", prompt);
        Assert.Contains("Title B", prompt);
        Assert.Contains("Title C", prompt);
        Assert.Contains("Summary A", prompt);
        Assert.Contains("Summary B", prompt);
        Assert.Contains("Summary C", prompt);
    }

    [Fact]
    public void BuildPrompt_ContextItemWithNoSummary_ShowsPlaceholder()
    {
        var context = new List<QdrantSearchResult>
        {
            new("c1", 0.9f, "Title Only", null),
        };

        var prompt = RagService.BuildPrompt("query", context);

        Assert.Contains("No summary available", prompt);
    }
}

