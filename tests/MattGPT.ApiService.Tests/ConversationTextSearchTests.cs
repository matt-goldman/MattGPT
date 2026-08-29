using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Covers the pure helpers shared by both repository implementations of
/// <c>SearchTextAsync</c>, so a change in one backend can't quietly alter the other.
/// </summary>
public class ConversationTextSearchTests
{
    private static StoredConversation MakeConversation(params StoredMessage[] messages)
        => new() { ConversationId = "c1", Title = "Test", LinearisedMessages = [.. messages] };

    private static StoredMessage Message(string role, string text, bool hidden = false, double? weight = null)
        => new() { Id = "m", Role = role, ContentType = "text", Parts = [text], IsHidden = hidden, Weight = weight };

    [Fact]
    public void Build_ReturnsExcerptAroundMatch_PrefixedWithRole()
    {
        var conversation = MakeConversation(Message("user", "How do I fix NoClassDefFoundError in the binding?"));

        var snippets = ConversationTextSnippets.Build(conversation, "NoClassDefFoundError");

        var snippet = Assert.Single(snippets);
        Assert.StartsWith("User: ", snippet);
        Assert.Contains("NoClassDefFoundError", snippet);
    }

    [Fact]
    public void Build_SkipsHiddenAndZeroWeightMessages()
    {
        var conversation = MakeConversation(
            Message("system", "gradle scaffolding", weight: 0.0),
            Message("user", "gradle question", hidden: true),
            Message("assistant", "gradle answer"));

        var snippets = ConversationTextSnippets.Build(conversation, "gradle");

        var snippet = Assert.Single(snippets);
        Assert.Contains("gradle answer", snippet);
    }

    [Fact]
    public void Build_HonoursMaxSnippets()
    {
        var conversation = MakeConversation(
            Message("user", "gradle one"),
            Message("assistant", "gradle two"),
            Message("user", "gradle three"));

        Assert.Equal(2, ConversationTextSnippets.Build(conversation, "gradle", maxSnippets: 2).Count);
    }

    [Fact]
    public void Build_IgnoresQuerySyntaxAndShortWords()
    {
        var conversation = MakeConversation(Message("user", "the android binding failed"));

        // Quotes and the leading - are query syntax, not text to look for; "to" is too short
        // to be a useful anchor. Only "binding" should locate the snippet.
        var snippets = ConversationTextSnippets.Build(conversation, "\"binding\" -android to");

        var snippet = Assert.Single(snippets);
        Assert.Contains("binding", snippet);
    }

    [Fact]
    public void Build_ReturnsEmptyWhenNothingMatchesLiterally()
    {
        var conversation = MakeConversation(Message("user", "we discussed dependency resolution"));

        // The database matches with stemming; this helper does not, so a stemmed-only hit
        // yields no snippet and callers fall back to the summary.
        Assert.Empty(ConversationTextSnippets.Build(conversation, "resolving"));
    }

    [Fact]
    public void Relative_RescalesAgainstBestHit()
    {
        Assert.Equal(1.0f, ConversationTextSearchRanking.Relative(4.0, 4.0));
        Assert.Equal(0.25f, ConversationTextSearchRanking.Relative(1.0, 4.0));
    }

    [Fact]
    public void Relative_ReturnsZeroWhenNoPositiveMaximum()
    {
        // Guards against divide-by-zero when a backend reports all-zero ranks.
        Assert.Equal(0f, ConversationTextSearchRanking.Relative(0.0, 0.0));
    }

    [Fact]
    public void MaxScore_ReturnsZeroForEmptyResults()
    {
        Assert.Equal(0d, ConversationTextSearchRanking.MaxScore([]));
        Assert.Equal(2.5d, ConversationTextSearchRanking.MaxScore(
        [
            new ConversationTextSearchResult("a", 1.0, null, null, []),
            new ConversationTextSearchResult("b", 2.5, null, null, []),
        ]));
    }
}
