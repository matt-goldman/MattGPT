using System.Text.Json;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Tests for <see cref="StoredMessage.From"/> covering all 12 content types
/// and the image_asset_pointer handling.
/// </summary>
public class StoredMessageTests
{
    // -----------------------------------------------------------------------
    // text
    // -----------------------------------------------------------------------

    [Fact]
    public void From_TextContentType_ExtractsStringParts()
    {
        var message = MakeMessage("m1", "user", "text", parts: ["Hello, world!"]);

        var stored = StoredMessage.From(message);

        Assert.Equal("text", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("Hello, world!", stored.Parts[0]);
    }

    [Fact]
    public void From_TextContentType_NullParts_ReturnsEmptyList()
    {
        var message = MakeMessage("m1", "user", "text");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // multimodal_text
    // -----------------------------------------------------------------------

    [Fact]
    public void From_MultimodalText_ExtractsStringParts()
    {
        var message = MakeMessage("m1", "user", "multimodal_text",
            parts: ["Check this out", "Some more text"]);

        var stored = StoredMessage.From(message);

        Assert.Equal("multimodal_text", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Check this out", stored.Parts[0]);
        Assert.Equal("Some more text", stored.Parts[1]);
    }

    [Fact]
    public void From_MultimodalText_ImageAssetPointer_ProducesPlaceholder()
    {
        var imageJson = """
            {
                "content_type": "image_asset_pointer",
                "asset_pointer": "file-service://file-abc123",
                "width": 1200,
                "height": 1600
            }
            """;
        var message = MakeMessageWithRawParts("m1", "user", "multimodal_text",
            [JsonDocument.Parse("\"Here is an image\"").RootElement,
             JsonDocument.Parse(imageJson).RootElement]);

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Here is an image", stored.Parts[0]);
        Assert.Equal("[Uploaded image: 1200×1600]", stored.Parts[1]);
    }

    [Fact]
    public void From_MultimodalText_DalleImage_IncludesDalleAnnotation()
    {
        var imageJson = """
            {
                "content_type": "image_asset_pointer",
                "asset_pointer": "sediment://dalle-abc",
                "width": 1024,
                "height": 1024,
                "dalle": { "prompt": "a cat", "seed": 42 }
            }
            """;
        var message = MakeMessageWithRawParts("m1", "assistant", "multimodal_text",
            [JsonDocument.Parse(imageJson).RootElement]);

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("[Image: 1024×1024, DALL-E generated]", stored.Parts[0]);
    }

    [Fact]
    public void From_MultimodalText_DalleImageNoDimensions_ShowsDalleOnly()
    {
        var imageJson = """
            {
                "content_type": "image_asset_pointer",
                "asset_pointer": "sediment://dalle-xyz",
                "dalle": { "prompt": "a dog" }
            }
            """;
        var message = MakeMessageWithRawParts("m1", "assistant", "multimodal_text",
            [JsonDocument.Parse(imageJson).RootElement]);

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("[Image: DALL-E generated]", stored.Parts[0]);
    }

    [Fact]
    public void From_MultimodalText_UploadedImageNoDimensions_ShowsUploadedOnly()
    {
        var imageJson = """
            {
                "content_type": "image_asset_pointer",
                "asset_pointer": "file-service://file-xyz"
            }
            """;
        var message = MakeMessageWithRawParts("m1", "user", "multimodal_text",
            [JsonDocument.Parse(imageJson).RootElement]);

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("[Uploaded image]", stored.Parts[0]);
    }

    // -----------------------------------------------------------------------
    // code
    // -----------------------------------------------------------------------

    [Fact]
    public void From_Code_ExtractsTextAndLanguage()
    {
        var message = MakeMessage("m1", "assistant", "code");
        message.Content.Text = "import pandas as pd\ndf = pd.read_csv('data.csv')";
        message.Content.Language = "python";

        var stored = StoredMessage.From(message);

        Assert.Equal("code", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("import pandas as pd\ndf = pd.read_csv('data.csv')", stored.Parts[0]);
        Assert.Equal("python", stored.Language);
    }

    [Fact]
    public void From_Code_NullText_ReturnsEmptyParts()
    {
        var message = MakeMessage("m1", "assistant", "code");
        message.Content.Language = "python";

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
        Assert.Equal("python", stored.Language);
    }

    // -----------------------------------------------------------------------
    // execution_output
    // -----------------------------------------------------------------------

    [Fact]
    public void From_ExecutionOutput_ExtractsText()
    {
        var message = MakeMessage("m1", "tool", "execution_output");
        message.Content.Text = "   x  y\n0  1  2\n1  3  4";

        var stored = StoredMessage.From(message);

        Assert.Equal("execution_output", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("   x  y\n0  1  2\n1  3  4", stored.Parts[0]);
    }

    [Fact]
    public void From_ExecutionOutput_EmptyText_ReturnsEmptyParts()
    {
        var message = MakeMessage("m1", "tool", "execution_output");
        message.Content.Text = "";

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // tether_quote
    // -----------------------------------------------------------------------

    [Fact]
    public void From_TetherQuote_ExtractsTextAndMetadata()
    {
        var message = MakeMessage("m1", "assistant", "tether_quote");
        message.Content.Text = "The quick brown fox jumps over the lazy dog.";
        message.Content.Url = "https://example.com/article";
        message.Content.Domain = "example.com";
        message.Content.Title = "Example Article";

        var stored = StoredMessage.From(message);

        Assert.Equal("tether_quote", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", stored.Parts[0]);
        Assert.Equal("[Source: Example Article]", stored.Parts[1]);
        Assert.Equal("https://example.com/article", stored.Url);
        Assert.Equal("example.com", stored.Domain);
        Assert.Equal("Example Article", stored.SourceTitle);
    }

    [Fact]
    public void From_TetherQuote_NoTitle_FallsToDomain()
    {
        var message = MakeMessage("m1", "assistant", "tether_quote");
        message.Content.Text = "Some quote";
        message.Content.Domain = "example.com";

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("[Source: example.com]", stored.Parts[1]);
    }

    [Fact]
    public void From_TetherQuote_EmptyTitle_FallsToDomain()
    {
        var message = MakeMessage("m1", "assistant", "tether_quote");
        message.Content.Text = "Some quote";
        message.Content.Title = "";
        message.Content.Domain = "example.com";

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("[Source: example.com]", stored.Parts[1]);
    }

    [Fact]
    public void From_TetherQuote_NoTitleNoDomain_NoSourceAnnotation()
    {
        var message = MakeMessage("m1", "assistant", "tether_quote");
        message.Content.Text = "Some quote";

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("Some quote", stored.Parts[0]);
    }

    // -----------------------------------------------------------------------
    // tether_browsing_display
    // -----------------------------------------------------------------------

    [Fact]
    public void From_TetherBrowsingDisplay_ExtractsResultAndSummary()
    {
        var message = MakeMessage("m1", "assistant", "tether_browsing_display");
        message.Content.Result = "Browse result content here";
        message.Content.Summary = "Summary of the browsing";

        var stored = StoredMessage.From(message);

        Assert.Equal("tether_browsing_display", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Browse result content here", stored.Parts[0]);
        Assert.Equal("Summary of the browsing", stored.Parts[1]);
    }

    [Fact]
    public void From_TetherBrowsingDisplay_OnlyResult_SinglePart()
    {
        var message = MakeMessage("m1", "assistant", "tether_browsing_display");
        message.Content.Result = "Browse result content here";

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("Browse result content here", stored.Parts[0]);
    }

    [Fact]
    public void From_TetherBrowsingDisplay_NeitherResultNorSummary_EmptyParts()
    {
        var message = MakeMessage("m1", "assistant", "tether_browsing_display");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // thoughts
    // -----------------------------------------------------------------------

    [Fact]
    public void From_Thoughts_ExtractsThoughtContent()
    {
        var message = MakeMessage("m1", "assistant", "thoughts");
        message.Content.Thoughts =
        [
            new ThoughtItem { Content = "Let me think about this...", Summary = "Thinking" },
            new ThoughtItem { Content = "Actually, the answer is 42.", Summary = "Concluded" },
        ];

        var stored = StoredMessage.From(message);

        Assert.Equal("thoughts", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Let me think about this...", stored.Parts[0]);
        Assert.Equal("Actually, the answer is 42.", stored.Parts[1]);
    }

    [Fact]
    public void From_Thoughts_SkipsEmptyContent()
    {
        var message = MakeMessage("m1", "assistant", "thoughts");
        message.Content.Thoughts =
        [
            new ThoughtItem { Content = "Valid thought", Summary = "OK" },
            new ThoughtItem { Content = "", Summary = "Empty" },
            new ThoughtItem { Content = null, Summary = "Null" },
        ];

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("Valid thought", stored.Parts[0]);
    }

    [Fact]
    public void From_Thoughts_NullThoughtsArray_EmptyParts()
    {
        var message = MakeMessage("m1", "assistant", "thoughts");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // reasoning_recap
    // -----------------------------------------------------------------------

    [Fact]
    public void From_ReasoningRecap_ExtractsContent()
    {
        var message = MakeMessage("m1", "assistant", "reasoning_recap");
        message.Content.ReasoningContent = "Thought for a couple of seconds";

        var stored = StoredMessage.From(message);

        Assert.Equal("reasoning_recap", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("Thought for a couple of seconds", stored.Parts[0]);
    }

    [Fact]
    public void From_ReasoningRecap_NullContent_EmptyParts()
    {
        var message = MakeMessage("m1", "assistant", "reasoning_recap");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // user_editable_context
    // -----------------------------------------------------------------------

    [Fact]
    public void From_UserEditableContext_ExtractsProfileAndInstructions()
    {
        var message = MakeMessage("m1", "system", "user_editable_context");
        message.Content.UserProfile = "I am a .NET developer.";
        message.Content.UserInstructions = "Be concise and use C# examples.";

        var stored = StoredMessage.From(message);

        Assert.Equal("user_editable_context", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("[User Profile] I am a .NET developer.", stored.Parts[0]);
        Assert.Equal("[User Instructions] Be concise and use C# examples.", stored.Parts[1]);
    }

    [Fact]
    public void From_UserEditableContext_OnlyProfile_SinglePart()
    {
        var message = MakeMessage("m1", "system", "user_editable_context");
        message.Content.UserProfile = "I am a .NET developer.";

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("[User Profile] I am a .NET developer.", stored.Parts[0]);
    }

    // -----------------------------------------------------------------------
    // system_error
    // -----------------------------------------------------------------------

    [Fact]
    public void From_SystemError_ExtractsNameAndText()
    {
        var message = MakeMessage("m1", "system", "system_error");
        message.Content.Name = "GetDownloadLinkError";
        message.Content.Text = "Could not retrieve the file.";

        var stored = StoredMessage.From(message);

        Assert.Equal("system_error", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("[Error: GetDownloadLinkError]", stored.Parts[0]);
        Assert.Equal("Could not retrieve the file.", stored.Parts[1]);
    }

    [Fact]
    public void From_SystemError_OnlyText_SinglePart()
    {
        var message = MakeMessage("m1", "system", "system_error");
        message.Content.Text = "Something went wrong.";

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("Something went wrong.", stored.Parts[0]);
    }

    // -----------------------------------------------------------------------
    // citable_code_output
    // -----------------------------------------------------------------------

    [Fact]
    public void From_CitableCodeOutput_ExtractsOutputStr()
    {
        var message = MakeMessage("m1", "tool", "citable_code_output");
        message.Content.OutputStr = "{\"result\": 42}";

        var stored = StoredMessage.From(message);

        Assert.Equal("citable_code_output", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("{\"result\": 42}", stored.Parts[0]);
    }

    [Fact]
    public void From_CitableCodeOutput_NullOutputStr_EmptyParts()
    {
        var message = MakeMessage("m1", "tool", "citable_code_output");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // computer_output
    // -----------------------------------------------------------------------

    [Fact]
    public void From_ComputerOutput_ExtractsStateTitleAndUrl()
    {
        var message = MakeMessage("m1", "tool", "computer_output");
        message.Content.State = new ComputerState
        {
            Type = "browser_state",
            Title = "Google Search",
            Url = "https://www.google.com",
        };

        var stored = StoredMessage.From(message);

        Assert.Equal("computer_output", stored.ContentType);
        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Google Search", stored.Parts[0]);
        Assert.Equal("https://www.google.com", stored.Parts[1]);
    }

    [Fact]
    public void From_ComputerOutput_NullState_EmptyParts()
    {
        var message = MakeMessage("m1", "tool", "computer_output");

        var stored = StoredMessage.From(message);

        Assert.Empty(stored.Parts);
    }

    // -----------------------------------------------------------------------
    // Unknown content type falls back to parts extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void From_UnknownContentType_FallsBackToPartsExtraction()
    {
        var message = MakeMessage("m1", "user", "some_future_type", parts: ["future content"]);

        var stored = StoredMessage.From(message);

        Assert.Equal("some_future_type", stored.ContentType);
        Assert.Single(stored.Parts);
        Assert.Equal("future content", stored.Parts[0]);
    }

    // -----------------------------------------------------------------------
    // General properties
    // -----------------------------------------------------------------------

    [Fact]
    public void From_PreservesIdRoleAndCreateTime()
    {
        var message = MakeMessage("msg-42", "assistant", "text", parts: ["Hi"]);
        message.CreateTime = 1700000000.0;

        var stored = StoredMessage.From(message);

        Assert.Equal("msg-42", stored.Id);
        Assert.Equal("assistant", stored.Role);
        Assert.Equal(1700000000.0, stored.CreateTime);
    }

    // -----------------------------------------------------------------------
    // JSON round-trip: verify deserialization feeds From() correctly
    // -----------------------------------------------------------------------

    [Fact]
    public void From_CodeContentType_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-code",
                "author": { "role": "assistant" },
                "content": {
                    "content_type": "code",
                    "language": "python",
                    "text": "print('hello')"
                },
                "create_time": 1700000000.0
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal("code", stored.ContentType);
        Assert.Equal("python", stored.Language);
        Assert.Single(stored.Parts);
        Assert.Equal("print('hello')", stored.Parts[0]);
    }

    [Fact]
    public void From_TetherQuote_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-tq",
                "author": { "role": "assistant" },
                "content": {
                    "content_type": "tether_quote",
                    "url": "https://example.com",
                    "domain": "example.com",
                    "title": "Example",
                    "text": "Quote text here"
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal("tether_quote", stored.ContentType);
        Assert.Equal("Quote text here", stored.Parts[0]);
        Assert.Equal("[Source: Example]", stored.Parts[1]);
        Assert.Equal("https://example.com", stored.Url);
        Assert.Equal("example.com", stored.Domain);
        Assert.Equal("Example", stored.SourceTitle);
    }

    [Fact]
    public void From_TetherBrowsingDisplay_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-tbd",
                "author": { "role": "assistant" },
                "content": {
                    "content_type": "tether_browsing_display",
                    "result": "Full page content",
                    "summary": "A brief summary"
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Full page content", stored.Parts[0]);
        Assert.Equal("A brief summary", stored.Parts[1]);
    }

    [Fact]
    public void From_Thoughts_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-thoughts",
                "author": { "role": "assistant" },
                "content": {
                    "content_type": "thoughts",
                    "thoughts": [
                        { "content": "First thought", "summary": "Thinking" },
                        { "content": "Second thought", "summary": "Still thinking" }
                    ]
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("First thought", stored.Parts[0]);
        Assert.Equal("Second thought", stored.Parts[1]);
    }

    [Fact]
    public void From_ReasoningRecap_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-recap",
                "author": { "role": "assistant" },
                "content": {
                    "content_type": "reasoning_recap",
                    "content": "Thought for 30 seconds"
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Single(stored.Parts);
        Assert.Equal("Thought for 30 seconds", stored.Parts[0]);
    }

    [Fact]
    public void From_UserEditableContext_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-uec",
                "author": { "role": "system" },
                "content": {
                    "content_type": "user_editable_context",
                    "user_profile": "I'm a developer",
                    "user_instructions": "Use code examples"
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("[User Profile] I'm a developer", stored.Parts[0]);
        Assert.Equal("[User Instructions] Use code examples", stored.Parts[1]);
    }

    [Fact]
    public void From_ComputerOutput_ViaJsonDeserialization()
    {
        var json = """
            {
                "id": "msg-comp",
                "author": { "role": "tool" },
                "content": {
                    "content_type": "computer_output",
                    "computer_id": "0",
                    "state": {
                        "type": "browser_state",
                        "url": "https://example.com",
                        "title": "Example Page"
                    }
                }
            }
            """;
        var message = JsonSerializer.Deserialize<Message>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var stored = StoredMessage.From(message);

        Assert.Equal(2, stored.Parts.Count);
        Assert.Equal("Example Page", stored.Parts[0]);
        Assert.Equal("https://example.com", stored.Parts[1]);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a Message with the given content type and optional string parts.
    /// </summary>
    private static Message MakeMessage(string id, string role, string contentType, string[]? parts = null)
    {
        var content = new Content { ContentType = contentType };

        if (parts is not null)
        {
            content.Parts = parts
                .Select(p => JsonSerializer.SerializeToElement(p))
                .ToList();
        }

        return new Message
        {
            Id = id,
            Author = new Author { Role = role },
            Content = content,
        };
    }

    /// <summary>
    /// Creates a Message with pre-built JsonElement parts (for testing image_asset_pointer etc.).
    /// </summary>
    private static Message MakeMessageWithRawParts(string id, string role, string contentType, List<JsonElement> parts)
    {
        return new Message
        {
            Id = id,
            Author = new Author { Role = role },
            Content = new Content
            {
                ContentType = contentType,
                Parts = parts,
            },
        };
    }

    // -----------------------------------------------------------------------
    // Weight and IsHidden
    // -----------------------------------------------------------------------

    [Fact]
    public void From_WeightCaptured()
    {
        var message = MakeMessage("m1", "system", "text", ["Custom instructions"]);
        message.Weight = 0.0;

        var stored = StoredMessage.From(message);

        Assert.Equal(0.0, stored.Weight);
    }

    [Fact]
    public void From_WeightNull_WhenNotPresent()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);

        var stored = StoredMessage.From(message);

        Assert.Null(stored.Weight);
    }

    [Fact]
    public void From_WeightOne_ForNormalMessage()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);
        message.Weight = 1.0;

        var stored = StoredMessage.From(message);

        Assert.Equal(1.0, stored.Weight);
    }

    [Fact]
    public void From_IsHidden_True_WhenMetadataIndicatesHidden()
    {
        var message = MakeMessage("m1", "system", "text", ["System prompt"]);
        message.Metadata = new MessageMetadata { IsVisuallyHiddenFromConversation = true };

        var stored = StoredMessage.From(message);

        Assert.True(stored.IsHidden);
    }

    [Fact]
    public void From_IsHidden_False_WhenMetadataIndicatesVisible()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);
        message.Metadata = new MessageMetadata { IsVisuallyHiddenFromConversation = false };

        var stored = StoredMessage.From(message);

        Assert.False(stored.IsHidden);
    }

    [Fact]
    public void From_IsHidden_False_WhenMetadataNull()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);
        // No metadata set

        var stored = StoredMessage.From(message);

        Assert.False(stored.IsHidden);
    }

    [Fact]
    public void From_IsHidden_False_WhenHiddenFlagNull()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);
        message.Metadata = new MessageMetadata { IsVisuallyHiddenFromConversation = null };

        var stored = StoredMessage.From(message);

        Assert.False(stored.IsHidden);
    }

    // -----------------------------------------------------------------------
    // Citations
    // -----------------------------------------------------------------------

    [Fact]
    public void From_ParsesCitationsFromMetadata()
    {
        var message = MakeMessage("m1", "assistant", "text", ["Answer text"]);
        message.Metadata = new MessageMetadata
        {
            Citations =
            [
                new MessageCitation
                {
                    StartIndex = 0,
                    EndIndex = 10,
                    FormatType = "tether_og",
                    Metadata = new CitationMetadata
                    {
                        Type = "webpage",
                        Title = "Example Page",
                        Url = "https://example.com",
                        Text = "Some cited text",
                    },
                },
            ],
        };

        var stored = StoredMessage.From(message);

        Assert.NotNull(stored.Citations);
        Assert.Single(stored.Citations);
        var c = stored.Citations[0];
        Assert.Equal(0, c.StartIndex);
        Assert.Equal(10, c.EndIndex);
        Assert.Equal("tether_og", c.FormatType);
        Assert.Equal("webpage", c.Type);
        Assert.Equal("Example Page", c.Name);
        Assert.Equal("https://example.com", c.Source);
        Assert.Equal("Some cited text", c.Text);
    }

    [Fact]
    public void From_NullCitations_CitationsPropertyNull()
    {
        var message = MakeMessage("m1", "user", "text", ["Hello"]);
        // No metadata / citations set

        var stored = StoredMessage.From(message);

        Assert.Null(stored.Citations);
    }

    // -----------------------------------------------------------------------
    // Content References
    // -----------------------------------------------------------------------

    [Fact]
    public void From_FiltersHiddenContentReferences()
    {
        var message = MakeMessage("m1", "assistant", "text", ["Answer"]);
        message.Metadata = new MessageMetadata
        {
            ContentReferences =
            [
                new MessageContentReference { Type = "hidden", Title = "Hidden ref" },
                new MessageContentReference { Type = "attribution", Title = "Visible ref", Url = "https://example.com" },
            ],
        };

        var stored = StoredMessage.From(message);

        Assert.NotNull(stored.ContentReferences);
        Assert.Single(stored.ContentReferences);
        Assert.Equal("attribution", stored.ContentReferences[0].Type);
    }

    [Fact]
    public void From_CapturesNonHiddenContentReferences()
    {
        var message = MakeMessage("m1", "assistant", "text", ["Answer"]);
        message.Metadata = new MessageMetadata
        {
            ContentReferences =
            [
                new MessageContentReference
                {
                    Type = "grouped_webpages",
                    Title = "Some Page",
                    MatchedText = "matched",
                    Snippet = "snip",
                    Url = "https://example.com",
                    Source = "web",
                },
            ],
        };

        var stored = StoredMessage.From(message);

        Assert.NotNull(stored.ContentReferences);
        Assert.Single(stored.ContentReferences);
        var r = stored.ContentReferences[0];
        Assert.Equal("grouped_webpages", r.Type);
        Assert.Equal("Some Page", r.Name);
        Assert.Equal("matched", r.MatchedText);
        Assert.Equal("snip", r.Snippet);
        Assert.Equal("https://example.com", r.Url);
        Assert.Equal("web", r.Source);
    }

    [Fact]
    public void From_AllHiddenContentReferences_ContentReferencesNull()
    {
        var message = MakeMessage("m1", "assistant", "text", ["Answer"]);
        message.Metadata = new MessageMetadata
        {
            ContentReferences =
            [
                new MessageContentReference { Type = "hidden", Title = "Hidden 1" },
                new MessageContentReference { Type = "hidden", Title = "Hidden 2" },
            ],
        };

        var stored = StoredMessage.From(message);

        Assert.Null(stored.ContentReferences);
    }
}
