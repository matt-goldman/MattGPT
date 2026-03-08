using System.Text;
using System.Text.Json;
using MattGPT.Contracts.Models;
using MattGPT.ApiService.Services;

namespace MattGPT.ApiService.Tests;

public class ConversationParserTests
{
    // -----------------------------------------------------------------------
    // Linearisation tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Linearise_EmptyMapping_ReturnsEmptyList()
    {
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>(),
            CurrentNode = null,
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Empty(result);
    }

    [Fact]
    public void Linearise_NullCurrentNode_ReturnsEmptyList()
    {
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>
            {
                ["node1"] = new MappingNode
                {
                    Id = "node1",
                    Message = MakeMessage("node1", "user", "hello"),
                    Parent = null,
                    Children = new List<string>()
                }
            },
            CurrentNode = null,
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Empty(result);
    }

    [Fact]
    public void Linearise_SingleNode_ReturnsSingleMessage()
    {
        var msg = MakeMessage("node1", "user", "hello");
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>
            {
                ["node1"] = new MappingNode
                {
                    Id = "node1",
                    Message = msg,
                    Parent = null,
                    Children = new List<string>()
                }
            },
            CurrentNode = "node1",
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Single(result);
        Assert.Equal("node1", result[0].Id);
    }

    [Fact]
    public void Linearise_LinearChain_ReturnsMessagesInChronologicalOrder()
    {
        // root (null message) -> node1 (user) -> node2 (assistant)
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>
            {
                ["root"] = new MappingNode
                {
                    Id = "root",
                    Message = null,
                    Parent = null,
                    Children = new List<string> { "node1" }
                },
                ["node1"] = new MappingNode
                {
                    Id = "node1",
                    Message = MakeMessage("node1", "user", "hello"),
                    Parent = "root",
                    Children = new List<string> { "node2" }
                },
                ["node2"] = new MappingNode
                {
                    Id = "node2",
                    Message = MakeMessage("node2", "assistant", "hi there"),
                    Parent = "node1",
                    Children = new List<string>()
                },
            },
            CurrentNode = "node2",
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Equal(2, result.Count);
        Assert.Equal("node1", result[0].Id);
        Assert.Equal("node2", result[1].Id);
    }

    [Fact]
    public void Linearise_BranchingConversation_ReturnsOnlyActiveBranch()
    {
        // root -> node1 -> node2a (active branch)
        //              \-> node2b (inactive branch)
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>
            {
                ["root"] = new MappingNode
                {
                    Id = "root",
                    Message = null,
                    Parent = null,
                    Children = new List<string> { "node1" }
                },
                ["node1"] = new MappingNode
                {
                    Id = "node1",
                    Message = MakeMessage("node1", "user", "hello"),
                    Parent = "root",
                    Children = new List<string> { "node2a", "node2b" }
                },
                ["node2a"] = new MappingNode
                {
                    Id = "node2a",
                    Message = MakeMessage("node2a", "assistant", "response A"),
                    Parent = "node1",
                    Children = new List<string>()
                },
                ["node2b"] = new MappingNode
                {
                    Id = "node2b",
                    Message = MakeMessage("node2b", "assistant", "response B"),
                    Parent = "node1",
                    Children = new List<string>()
                },
            },
            CurrentNode = "node2a",
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Equal(2, result.Count);
        Assert.Equal("node1", result[0].Id);
        Assert.Equal("node2a", result[1].Id);
        Assert.DoesNotContain(result, m => m.Id == "node2b");
    }

    [Fact]
    public void Linearise_RootNodeHasNullMessage_RootExcludedFromResult()
    {
        // Root node has no message; only the child node should be included.
        var conversation = new Conversation
        {
            Id = "conv1",
            Mapping = new Dictionary<string, MappingNode>
            {
                ["root"] = new MappingNode
                {
                    Id = "root",
                    Message = null,
                    Parent = null,
                    Children = new List<string> { "node1" }
                },
                ["node1"] = new MappingNode
                {
                    Id = "node1",
                    Message = MakeMessage("node1", "user", "hello"),
                    Parent = "root",
                    Children = new List<string>()
                },
            },
            CurrentNode = "node1",
        };

        var result = ConversationParser.Linearise(conversation);

        Assert.Single(result);
        Assert.Equal("node1", result[0].Id);
    }

    // -----------------------------------------------------------------------
    // Parsing tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ParseAsync_EmptyArray_YieldsNoConversations()
    {
        var json = "[]";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ParseAsync_SingleConversation_YieldsConversationWithMetadata()
    {
        var json = """
            [
              {
                "id": "abc123",
                "title": "Test conversation",
                "create_time": 1700000000.0,
                "update_time": 1700000100.0,
                "default_model_slug": "gpt-4o",
                "current_node": "node1",
                "mapping": {
                  "node1": {
                    "id": "node1",
                    "parent": null,
                    "children": [],
                    "message": {
                      "id": "node1",
                      "author": { "role": "user" },
                      "content": { "content_type": "text", "parts": ["Hello!"] },
                      "create_time": 1700000000.0
                    }
                  }
                }
              }
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Single(results);
        var conv = results[0];
        Assert.Equal("abc123", conv.Id);
        Assert.Equal("Test conversation", conv.Title);
        Assert.Equal(1700000000.0, conv.CreateTime);
        Assert.Equal("gpt-4o", conv.DefaultModelSlug);
        Assert.Single(conv.Messages);
        Assert.Equal("user", conv.Messages[0].Author.Role);
    }

    [Fact]
    public async Task ParseAsync_ConversationWithMetadataFields_CapturesAllFields()
    {
        var json = """
            [
              {
                "id": "meta-conv",
                "title": "Custom GPT conversation",
                "create_time": 1700000000.0,
                "update_time": 1700000100.0,
                "gizmo_id": "g-abc123",
                "gizmo_type": "snorlax",
                "conversation_template_id": "tmpl-xyz",
                "is_do_not_remember": true,
                "memory_scope": "project_enabled",
                "is_archived": true,
                "current_node": "node1",
                "mapping": {
                  "node1": {
                    "id": "node1",
                    "parent": null,
                    "children": [],
                    "message": {
                      "id": "node1",
                      "author": { "role": "user" },
                      "content": { "content_type": "text", "parts": ["Hello!"] },
                      "create_time": 1700000000.0
                    }
                  }
                }
              }
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Single(results);
        var conv = results[0];
        Assert.Equal("g-abc123", conv.GizmoId);
        Assert.Equal("snorlax", conv.GizmoType);
        Assert.Equal("tmpl-xyz", conv.ConversationTemplateId);
        Assert.True(conv.IsDoNotRemember);
        Assert.Equal("project_enabled", conv.MemoryScope);
        Assert.True(conv.IsArchived);
    }

    [Fact]
    public async Task ParseAsync_ConversationWithoutOptionalMetadata_NullFields()
    {
        var json = """
            [
              {
                "id": "plain-conv",
                "title": "Standard conversation",
                "current_node": "node1",
                "mapping": {
                  "node1": {
                    "id": "node1",
                    "parent": null,
                    "children": [],
                    "message": {
                      "id": "node1",
                      "author": { "role": "user" },
                      "content": { "content_type": "text", "parts": ["Hello!"] }
                    }
                  }
                }
              }
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Single(results);
        var conv = results[0];
        Assert.Null(conv.GizmoId);
        Assert.Null(conv.GizmoType);
        Assert.Null(conv.ConversationTemplateId);
        Assert.Null(conv.IsDoNotRemember);
        Assert.Null(conv.MemoryScope);
        Assert.Null(conv.IsArchived);
    }

    [Fact]
    public void StoredConversation_From_MapsMetadataFields()
    {
        var parsed = new ParsedConversation
        {
            Id = "conv-meta",
            Title = "Test",
            GizmoId = "g-test",
            GizmoType = "gpt",
            ConversationTemplateId = "tmpl-test",
            IsDoNotRemember = true,
            MemoryScope = "global_enabled",
            IsArchived = false,
            Messages = [],
        };

        var stored = StoredConversation.From(parsed);

        Assert.Equal("g-test", stored.GizmoId);
        Assert.Equal("gpt", stored.GizmoType);
        Assert.Equal("tmpl-test", stored.ConversationTemplateId);
        Assert.True(stored.IsDoNotRemember);
        Assert.Equal("global_enabled", stored.MemoryScope);
        Assert.False(stored.IsArchived);
    }

    [Fact]
    public async Task ParseAsync_MultipleConversations_YieldsAll()
    {
        var json = """
            [
              {
                "id": "conv1",
                "title": "First",
                "current_node": "n1",
                "mapping": {
                  "n1": {
                    "id": "n1", "parent": null, "children": [],
                    "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text" } }
                  }
                }
              },
              {
                "id": "conv2",
                "title": "Second",
                "current_node": "n2",
                "mapping": {
                  "n2": {
                    "id": "n2", "parent": null, "children": [],
                    "message": { "id": "n2", "author": { "role": "assistant" }, "content": { "content_type": "text" } }
                  }
                }
              }
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Equal(2, results.Count);
        Assert.Equal("conv1", results[0].Id);
        Assert.Equal("conv2", results[1].Id);
    }

    [Fact]
    public async Task ParseAsync_ConversationWithBranching_LinearisesActiveBranch()
    {
        var json = """
            [
              {
                "id": "branch-conv",
                "title": "Branching",
                "current_node": "leaf-active",
                "mapping": {
                  "root": {
                    "id": "root", "parent": null, "children": ["n1"],
                    "message": null
                  },
                  "n1": {
                    "id": "n1", "parent": "root", "children": ["leaf-active", "leaf-inactive"],
                    "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text" } }
                  },
                  "leaf-active": {
                    "id": "leaf-active", "parent": "n1", "children": [],
                    "message": { "id": "leaf-active", "author": { "role": "assistant" }, "content": { "content_type": "text" } }
                  },
                  "leaf-inactive": {
                    "id": "leaf-inactive", "parent": "n1", "children": [],
                    "message": { "id": "leaf-inactive", "author": { "role": "assistant" }, "content": { "content_type": "text" } }
                  }
                }
              }
            ]
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parser = new ConversationParser();

        var results = new List<ParsedConversation>();
        await foreach (var c in parser.ParseAsync(stream))
            results.Add(c);

        Assert.Single(results);
        var messages = results[0].Messages;
        Assert.Equal(2, messages.Count);
        Assert.Equal("n1", messages[0].Id);
        Assert.Equal("leaf-active", messages[1].Id);
        Assert.DoesNotContain(messages, m => m.Id == "leaf-inactive");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Message MakeMessage(string id, string role, string text = "")
    {
        var partsJson = JsonSerializer.SerializeToElement(new[] { text });
        return new Message
        {
            Id = id,
            Author = new Author { Role = role },
            Content = new Content
            {
                ContentType = "text",
                Parts = new List<JsonElement> { partsJson[0] },
            }
        };
    }
}
