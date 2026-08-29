using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

public class KeywordSearchMemoriesToolTests
{
    private static StoredConversation MakeConversation(
        string id, string title, string? summary = null, params string[] messages)
    {
        return new StoredConversation
        {
            ConversationId = id,
            Title = title,
            Summary = summary,
            LinearisedMessages =
            [
                .. messages.Select((m, i) => new StoredMessage
                {
                    Id = $"m{i}",
                    Role = i % 2 == 0 ? "user" : "assistant",
                    ContentType = "text",
                    Parts = [m],
                }),
            ],
            ProcessingStatus = ConversationProcessingStatus.Embedded,
        };
    }

    private static KeywordSearchMemoriesTool CreateTool(
        FakeConversationRepository? repository = null, RagOptions? options = null)
    {
        return new KeywordSearchMemoriesTool(
            repository ?? new FakeConversationRepository(),
            Options.Create(options ?? new RagOptions()),
            new NullCurrentUserService(),
            NullLogger<KeywordSearchMemoriesTool>.Instance);
    }

    [Fact]
    public async Task SearchMemoriesKeywordAsync_NoMatches_PointsAtSemanticSearch()
    {
        var tool = CreateTool();

        var result = await tool.SearchMemoriesKeywordAsync("nothing-matches-this");

        Assert.Contains("No past conversations contain those exact words", result);
        Assert.Contains("search_memories", result);
        Assert.Empty(tool.LastSources);
    }

    [Fact]
    public async Task SearchMemoriesKeywordAsync_WithMatches_ReturnsSnippetsAndSources()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([
            MakeConversation("c1", "Binding work", "Android bindings",
                "How do I fix NoClassDefFoundError in the binding?",
                "Add the missing Maven dependency to the Gradle file."),
        ]);

        var tool = CreateTool(repo);

        var result = await tool.SearchMemoriesKeywordAsync("NoClassDefFoundError");

        Assert.Contains("Found 1 past conversation(s) containing the search terms", result);
        Assert.Contains("Binding work", result);
        Assert.Contains("Matching excerpts:", result);
        Assert.Contains("NoClassDefFoundError", result);
        Assert.Single(tool.LastSources);
        Assert.Equal("c1", tool.LastSources[0].ConversationId);
    }

    [Fact]
    public async Task SearchMemoriesKeywordAsync_NormalisesSourceScoresToRelativeRank()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([
            MakeConversation("c1", "Gradle and Maven", null, "gradle maven dependency"),
            MakeConversation("c2", "Gradle only", null, "gradle build"),
        ]);

        var tool = CreateTool(repo);

        await tool.SearchMemoriesKeywordAsync("gradle maven");

        // The fake ranks by matched-term count (2 and 1), which the tool rescales so the
        // best hit is 1.0 and the rest are fractions of it - keeping keyword scores in the
        // same 0-1 range as the cosine similarities they get merged with.
        Assert.Equal(2, tool.LastSources.Count);
        Assert.Equal(1.0f, tool.LastSources[0].Score);
        Assert.Equal(0.5f, tool.LastSources[1].Score);
    }

    [Fact]
    public async Task SearchMemoriesKeywordAsync_ClampsMaxResultsAndUsesConfiguredDefault()
    {
        var repo = new FakeConversationRepository();
        repo.Seed([.. Enumerable.Range(1, 8).Select(i =>
            MakeConversation($"c{i}", $"Title {i}", null, "gradle"))]);

        var tool = CreateTool(repo, new RagOptions { ToolMaxResults = 2 });

        var defaulted = await tool.SearchMemoriesKeywordAsync("gradle");
        Assert.Contains("Found 2 past conversation(s)", defaulted);

        // Above the hard ceiling of 10, and the fake only holds 8.
        var clamped = await tool.SearchMemoriesKeywordAsync("gradle", maxResults: 50);
        Assert.Contains("Found 8 past conversation(s)", clamped);
    }

    [Fact]
    public async Task SearchMemoriesKeywordAsync_RaisesStartAndCompleteCallbacks()
    {
        var events = new List<string>();
        var tool = CreateTool();
        tool.OnStarted = name => events.Add($"start:{name}");
        tool.OnCompleted = name => events.Add($"end:{name}");

        await tool.SearchMemoriesKeywordAsync("anything");

        Assert.Equal(["start:search_memories_keyword", "end:search_memories_keyword"], events);
    }

    [Fact]
    public void CreateAIFunction_ExposesKeywordToolName()
    {
        var aiFunction = CreateTool().CreateAIFunction();

        Assert.Equal("search_memories_keyword", aiFunction.Name);
    }
}
