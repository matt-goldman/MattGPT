using MattGPT.ApiService.Extensions;
using MattGPT.ApiService.Services;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Covers the exclusion of tool traffic and scaffolding from everything a model or a user sees
/// (defect 051). A ChatGPT export's <c>tool</c> and <c>system</c> messages mostly carry
/// <c>weight: 1.0</c> and no hidden flag, so the weight/hidden filter these paths used before did
/// not touch them. The tool results stay in the store; they just must not be rendered.
/// </summary>
public class ToolTrafficFilterTests
{
    /// <summary>A tool result as a real export carries it: full weight, not hidden.</summary>
    private static StoredMessage ToolResult(string text = "FILE CONTENTS: a very long uploaded file")
        => new() { Id = "t", Role = "tool", ContentType = "tether_quote", Parts = [text], Weight = 1.0 };

    private static StoredMessage SystemMessage(string text = "system scaffolding")
        => new() { Id = "s", Role = "system", ContentType = "text", Parts = [text], Weight = 1.0 };

    private static StoredMessage Dialogue(string role, string text)
        => new() { Id = role, Role = role, ContentType = "text", Parts = [text], Weight = 1.0 };

    private static StoredConversation Conversation(params StoredMessage[] messages)
        => new() { ConversationId = "c1", Title = "Test", LinearisedMessages = [.. messages] };

    // --- The predicate itself ---

    [Theory]
    [InlineData("user", true)]
    [InlineData("assistant", true)]
    [InlineData("tool", false)]
    [InlineData("system", false)]
    public void IsConversational_FiltersByRole_EvenAtFullWeightAndNotHidden(string role, bool expected)
    {
        var message = new StoredMessage { Id = "m", Role = role, ContentType = "text", Parts = ["x"], Weight = 1.0 };

        Assert.Equal(expected, message.IsConversational);
    }

    [Fact]
    public void IsConversational_StillExcludesHiddenAndZeroWeightDialogue()
    {
        var hidden = new StoredMessage { Id = "m", Role = "assistant", ContentType = "text", Parts = ["x"], IsHidden = true };
        var zeroWeight = new StoredMessage { Id = "m", Role = "user", ContentType = "text", Parts = ["x"], Weight = 0.0 };

        Assert.False(hidden.IsConversational);
        Assert.False(zeroWeight.IsConversational);
    }

    [Fact]
    public void ConversationalMessages_PreservesOrderOfDialogue()
    {
        var conversation = Conversation(
            Dialogue("user", "first"),
            ToolResult(),
            SystemMessage(),
            Dialogue("assistant", "second"));

        Assert.Equal(["first", "second"], conversation.ConversationalMessages.Select(m => m.Parts[0]));
    }

    // --- Embedding ---

    [Fact]
    public void ToEmbeddingText_ExcludesToolAndSystemMessages()
    {
        var conversation = Conversation(
            Dialogue("user", "How do I bind an Android AAR?"),
            ToolResult(),
            SystemMessage(),
            Dialogue("assistant", "Use native library interop."));

        var text = conversation.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.DoesNotContain("FILE CONTENTS", text);
        Assert.DoesNotContain("system scaffolding", text);
        Assert.Contains("user: How do I bind an Android AAR?", text);
        Assert.Contains("assistant: Use native library interop.", text);
    }

    // --- Prompt / tool-result excerpt (the path that had no filter at all) ---

    [Fact]
    public void ToExcerpt_ExcludesToolAndSystemMessages()
    {
        var conversation = Conversation(
            Dialogue("user", "question"),
            ToolResult(),
            SystemMessage(),
            Dialogue("assistant", "answer"));

        var excerpt = conversation.ToExcerpt();

        Assert.DoesNotContain("FILE CONTENTS", excerpt);
        Assert.DoesNotContain("system scaffolding", excerpt);
        Assert.Contains("User: question", excerpt);
        Assert.Contains("Assistant: answer", excerpt);
    }

    [Fact]
    public void ToExcerpt_LongToolResultDoesNotConsumeTheBudget()
    {
        // Before the fix a single tool result this size filled the excerpt and pushed the
        // dialogue out entirely.
        var conversation = Conversation(
            ToolResult(new string('x', 50_000)),
            Dialogue("user", "the actual question"),
            Dialogue("assistant", "the actual answer"));

        var excerpt = conversation.ToExcerpt();

        Assert.DoesNotContain("xxxx", excerpt);
        Assert.DoesNotContain("[conversation truncated]", excerpt);
        Assert.Contains("User: the actual question", excerpt);
        Assert.Contains("Assistant: the actual answer", excerpt);
    }

    // --- Digest / summary ---

    [Fact]
    public void BuildTranscript_ExcludesToolAndSystemMessages()
    {
        var transcript = SummarisationService.BuildTranscript(
            [
                Dialogue("user", "question"),
                ToolResult(),
                SystemMessage(),
                Dialogue("assistant", "answer"),
            ],
            budget: 10_000);

        Assert.NotNull(transcript);
        Assert.DoesNotContain("FILE CONTENTS", transcript);
        Assert.DoesNotContain("system scaffolding", transcript);
        Assert.Contains("user: question", transcript);
        Assert.Contains("assistant: answer", transcript);
    }

    [Fact]
    public void BuildTranscript_ToolTrafficOnly_ReturnsNull()
    {
        var transcript = SummarisationService.BuildTranscript([ToolResult(), SystemMessage()], budget: 10_000);

        Assert.Null(transcript);
    }

    // --- Text-search snippets ---

    [Fact]
    public void TextSearchSnippets_NeverComeFromToolResults()
    {
        var conversation = Conversation(
            ToolResult("FILE CONTENTS: binding aar instructions everywhere"),
            Dialogue("user", "how do I do an aar binding"));

        var snippets = ConversationTextSnippets.Build(conversation, "binding", maxSnippets: 3);

        Assert.All(snippets, s => Assert.DoesNotContain("FILE CONTENTS", s));
        Assert.Single(snippets);
        Assert.StartsWith("User:", snippets[0]);
    }

    // --- The data is still kept ---

    [Fact]
    public void ToolTrafficRemainsInTheStore()
    {
        var conversation = Conversation(Dialogue("user", "q"), ToolResult(), SystemMessage());

        // Filtering happens on read, not on import: the raw messages are all still there so a
        // future files / tool-results view (issue 052) can surface them.
        Assert.Equal(3, conversation.LinearisedMessages.Count);
        Assert.Contains(conversation.LinearisedMessages, m => m.Role == "tool");
        Assert.Contains(conversation.LinearisedMessages, m => m.Role == "system");
    }
}
