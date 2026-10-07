using System.Text.Json;
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
        => new() { Id = "t", Role = "tool", ContentType = "tether_quote", Parts = [text], Weight = 1.0, Recipient = "assistant" };

    private static StoredMessage SystemMessage(string text = "system scaffolding")
        => new() { Id = "s", Role = "system", ContentType = "text", Parts = [text], Weight = 1.0, Recipient = "all" };

    private static StoredMessage Dialogue(string role, string text)
        => new() { Id = role, Role = role, ContentType = "text", Parts = [text], Weight = 1.0, Recipient = "all" };

    /// <summary>An assistant message addressed to a tool: a tool call, not a reply to the user.</summary>
    private static StoredMessage ToolCall(string recipient = "python", string text = "print(secret_scaffolding)")
        => new() { Id = "c", Role = "assistant", ContentType = "code", Parts = [text], Weight = 1.0, Recipient = recipient };

    private static StoredMessage Reasoning(string contentType = "thoughts", string text = "internal deliberation")
        => new() { Id = "r", Role = "assistant", ContentType = contentType, Parts = [text], Weight = 1.0, Recipient = "all" };

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
        var message = new StoredMessage { Id = "m", Role = role, ContentType = "text", Parts = ["x"], Weight = 1.0, Recipient = "all" };

        Assert.Equal(expected, message.IsConversational);
    }

    [Theory]
    [InlineData("all", true)]
    [InlineData(null, true)]            // pre-existing documents, and chat-session projections
    [InlineData("python", false)]
    [InlineData("bio", false)]
    [InlineData("web.run", false)]
    [InlineData("canmore.create_textdoc", false)]
    [InlineData("file_search.msearch", false)]
    public void IsConversational_ExcludesAssistantMessagesAddressedToATool(string? recipient, bool expected)
    {
        var message = new StoredMessage { Id = "m", Role = "assistant", ContentType = "text", Parts = ["x"], Weight = 1.0, Recipient = recipient };

        Assert.Equal(expected, message.IsConversational);
    }

    [Theory]
    [InlineData("thoughts")]
    [InlineData("reasoning_recap")]
    public void IsConversational_ExcludesReasoningTraces(string contentType)
    {
        Assert.False(Reasoning(contentType).IsConversational);
    }

    [Fact]
    public void IsConversational_NullRecipient_FallsBackToRole()
    {
        // A document imported before Recipient was captured must keep working: the role check
        // still excludes tool traffic, and dialogue is not silently dropped.
        var dialogue = new StoredMessage { Id = "m", Role = "user", ContentType = "text", Parts = ["x"], Weight = 1.0, Recipient = null };
        var tool = new StoredMessage { Id = "m", Role = "tool", ContentType = "tether_quote", Parts = ["x"], Weight = 1.0, Recipient = null };

        Assert.True(dialogue.IsConversational);
        Assert.False(tool.IsConversational);
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
            ToolCall(),
            Reasoning(),
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
            ToolCall(),
            Reasoning(),
            Dialogue("assistant", "Use native library interop."));

        var text = conversation.ToEmbeddingText(EmbeddingService.MaxEmbeddingTextChars);

        Assert.DoesNotContain("FILE CONTENTS", text);
        Assert.DoesNotContain("system scaffolding", text);
        Assert.DoesNotContain("secret_scaffolding", text);
        Assert.DoesNotContain("internal deliberation", text);
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
            ToolCall(),
            Reasoning(),
            Dialogue("assistant", "answer"));

        var excerpt = conversation.ToExcerpt();

        Assert.DoesNotContain("FILE CONTENTS", excerpt);
        Assert.DoesNotContain("system scaffolding", excerpt);
        Assert.DoesNotContain("secret_scaffolding", excerpt);
        Assert.DoesNotContain("internal deliberation", excerpt);
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
                ToolCall(),
                Reasoning(),
                Dialogue("assistant", "answer"),
            ],
            budget: 10_000);

        Assert.NotNull(transcript);
        Assert.DoesNotContain("FILE CONTENTS", transcript);
        Assert.DoesNotContain("system scaffolding", transcript);
        Assert.DoesNotContain("secret_scaffolding", transcript);
        Assert.DoesNotContain("internal deliberation", transcript);
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

    // --- The recipient actually arrives from the export ---

    [Fact]
    public void From_CapturesRecipient()
    {
        var message = new Message
        {
            Id        = "m1",
            Author    = new Author { Role = "assistant" },
            Content   = new Content { ContentType = "code", Text = "print(1)" },
            Recipient = "python",
            Weight    = 1.0,
        };

        var stored = StoredMessage.From(message);

        Assert.Equal("python", stored.Recipient);
        Assert.False(stored.IsConversational);
    }

    [Fact]
    public void From_RecipientAll_IsConversational()
    {
        var message = new Message
        {
            Id        = "m1",
            Author    = new Author { Role = "assistant" },
            Content   = new Content
            {
                ContentType = "text",
                Parts       = [JsonSerializer.SerializeToElement("a reply")],
            },
            Recipient = "all",
            Weight    = 1.0,
        };

        Assert.True(StoredMessage.From(message).IsConversational);
    }

    [Fact]
    public void ChatSessionProjection_IsConversational()
    {
        // MattGPT's own sessions are dialogue by construction and must survive the filter,
        // otherwise completed sessions would embed as empty (ADR-013).
        var session = new ChatSession
        {
            Title = "Session",
            Messages =
            [
                new ChatSessionMessage { Role = "user", Content = "q" },
                new ChatSessionMessage { Role = "assistant", Content = "a" },
            ],
        };

        var projection = StoredConversation.FromChatSession(session, summary: null);

        Assert.Equal(2, projection.ConversationalMessages.Count());
        Assert.All(projection.LinearisedMessages, m => Assert.Equal("all", m.Recipient));
    }

    // --- The data is still kept ---

    [Fact]
    public void ToolTrafficRemainsInTheStore()
    {
        var conversation = Conversation(Dialogue("user", "q"), ToolResult(), SystemMessage(), ToolCall(), Reasoning());

        // Filtering happens on read, not on import: the raw messages are all still there so a
        // future files / tool-results view (issue 052) can surface them.
        Assert.Equal(5, conversation.LinearisedMessages.Count);
        Assert.Contains(conversation.LinearisedMessages, m => m.Role == "tool");
        Assert.Contains(conversation.LinearisedMessages, m => m.Role == "system");
        Assert.Contains(conversation.LinearisedMessages, m => m.Recipient == "python");
        Assert.Contains(conversation.LinearisedMessages, m => m.ContentType == "thoughts");
    }
}
