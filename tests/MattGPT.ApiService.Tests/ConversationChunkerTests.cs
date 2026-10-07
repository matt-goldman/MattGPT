using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Covers the chunk unit each strategy produces, the addresses chunks carry, and read-time neighbour
/// expansion.
/// </summary>
public class ConversationChunkerTests
{
    private static StoredMessage Msg(string role, string text, double? weight = 1.0, bool hidden = false) => new()
    {
        Id          = Guid.NewGuid().ToString(),
        Role        = role,
        ContentType = "text",
        Parts       = [text],
        Weight      = weight,
        IsHidden    = hidden,
        CreateTime  = 1_700_000_000,
    };

    private static StoredConversation Conversation(params StoredMessage[] messages) => new()
    {
        ConversationId     = "c1",
        Title              = "Handheld power",
        Summary            = "A digest of the conversation.",
        CreateTime         = 1_700_000_000,
        LinearisedMessages = [.. messages],
    };

    private static ConversationChunker Chunker(
        ChunkingStrategy strategy, int maxChunkChars = 2_000, int neighbourWindow = 1)
        => new(Options.Create(new RagOptions
        {
            ChunkingStrategy = strategy,
            MaxChunkChars    = maxChunkChars,
            NeighbourWindow  = neighbourWindow,
        }));

    // ── Chunk units ──

    [Fact]
    public void WholeConversation_ProducesOneChunkCoveringEveryMessage()
    {
        var conversation = Conversation(Msg("user", "Which brick?"), Msg("assistant", "The 65W one."));

        var chunk = Assert.Single(Chunker(ChunkingStrategy.WholeConversation).Chunk(conversation));

        Assert.Equal(0, chunk.Ordinal);
        Assert.Equal(0, chunk.StartMessageIndex);
        Assert.Equal(1, chunk.EndMessageIndex);
        Assert.Equal(ChunkingStrategy.WholeConversation, chunk.Strategy);
        Assert.Contains("Title: Handheld power", chunk.EmbeddingText);
        Assert.Contains("Which brick?", chunk.EmbeddingText);
        Assert.Contains("The 65W one.", chunk.EmbeddingText);
    }

    [Fact]
    public void Message_ProducesOneChunkPerVisibleMessage_WithContiguousOrdinals()
    {
        var conversation = Conversation(
            Msg("system", "scaffolding", weight: 0.0),
            Msg("user", "Which brick?"),
            Msg("assistant", "The 65W one."),
            Msg("user", "hidden", hidden: true),
            Msg("user", "   "));

        var chunks = Chunker(ChunkingStrategy.Message).Chunk(conversation);

        Assert.Equal(2, chunks.Count);
        Assert.Equal([0, 1], chunks.Select(c => c.Ordinal));
        // The address is the index in the conversation, not the index among the chunks.
        Assert.Equal([1, 2], chunks.Select(c => c.StartMessageIndex));
        Assert.Equal([1, 2], chunks.Select(c => c.EndMessageIndex));
        Assert.Equal(["user", "assistant"], chunks.Select(c => c.Actor));
        Assert.All(chunks, c => Assert.Null(c.ParentOrdinal));
        Assert.Contains("Which brick?", chunks[0].EmbeddingText);
        Assert.DoesNotContain("The 65W one.", chunks[0].EmbeddingText);
    }

    [Fact]
    public void Message_PrefixesEachChunkWithTitleAndDigest()
    {
        var conversation = Conversation(Msg("user", "Which brick?"), Msg("assistant", "The 65W one."));

        var chunks = Chunker(ChunkingStrategy.Message).Chunk(conversation);

        Assert.All(chunks, c =>
        {
            Assert.Contains("Title: Handheld power", c.EmbeddingText);
            Assert.Contains("Summary: A digest of the conversation.", c.EmbeddingText);
        });
    }

    [Fact]
    public void Message_PrefixIsCappedSoItCannotCrowdOutTheChunk()
    {
        var conversation = Conversation(Msg("user", "Which brick?"));
        conversation.Summary = new string('s', 5_000);

        var chunk = Assert.Single(Chunker(ChunkingStrategy.Message, maxChunkChars: 200).Chunk(conversation));

        Assert.True(chunk.EmbeddingText.Length <= 200, $"chunk was {chunk.EmbeddingText.Length} chars");
        Assert.Contains("Which brick?", chunk.EmbeddingText);
    }

    [Fact]
    public void Exchange_GroupsAUserTurnWithTheTurnsThatFollowIt()
    {
        var conversation = Conversation(
            Msg("user", "Which brick?"),
            Msg("assistant", "The 65W one."),
            Msg("tool", "looked it up"),
            Msg("user", "And the cable?"),
            Msg("assistant", "Any 100W one."));

        var chunks = Chunker(ChunkingStrategy.Exchange).Chunk(conversation);

        Assert.Equal(2, chunks.Count);
        Assert.Equal((0, 2), (chunks[0].StartMessageIndex, chunks[0].EndMessageIndex));
        Assert.Equal((3, 4), (chunks[1].StartMessageIndex, chunks[1].EndMessageIndex));
        Assert.Contains("looked it up", chunks[0].EmbeddingText);
        Assert.DoesNotContain("And the cable?", chunks[0].EmbeddingText);
        // An exchange mixes roles, so no single actor owns it.
        Assert.Null(chunks[0].Actor);
    }

    [Fact]
    public void Exchange_MessagesBeforeTheFirstUserTurnFormTheirOwnChunk()
    {
        var conversation = Conversation(
            Msg("assistant", "Welcome back."),
            Msg("user", "Which brick?"),
            Msg("assistant", "The 65W one."));

        var chunks = Chunker(ChunkingStrategy.Exchange).Chunk(conversation);

        Assert.Equal(2, chunks.Count);
        Assert.Equal((0, 0), (chunks[0].StartMessageIndex, chunks[0].EndMessageIndex));
        Assert.Equal("assistant", chunks[0].Actor);
        Assert.Equal((1, 2), (chunks[1].StartMessageIndex, chunks[1].EndMessageIndex));
    }

    [Fact]
    public void AUnitLongerThanTheBudget_IsSplitIntoPartsThatNameTheirParent()
    {
        var conversation = Conversation(
            Msg("user", "short"),
            Msg("assistant", string.Join('\n', Enumerable.Repeat(new string('x', 90), 20))));

        var chunks = Chunker(ChunkingStrategy.Message, maxChunkChars: 300).Chunk(conversation);

        Assert.True(chunks.Count > 2, $"expected the long message to split; got {chunks.Count} chunks");
        Assert.All(chunks, c => Assert.True(c.EmbeddingText.Length <= 300));

        var parts = chunks.Where(c => c.StartMessageIndex == 1).ToList();
        Assert.True(parts.Count > 1);
        Assert.Null(parts[0].ParentOrdinal);
        // Every later part points at the first part of the unit, so a split is visible in the address.
        Assert.All(parts.Skip(1), p => Assert.Equal(parts[0].Ordinal, p.ParentOrdinal));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Ordinal));
    }

    [Fact]
    public void AConversationWithNoEmbeddableContent_ProducesNoChunks()
    {
        var conversation = Conversation(Msg("user", "hidden", hidden: true), Msg("assistant", "   "));
        conversation.Title = null;
        conversation.Summary = null;

        Assert.Empty(Chunker(ChunkingStrategy.Message).Chunk(conversation));
        Assert.Empty(Chunker(ChunkingStrategy.WholeConversation).Chunk(conversation));
    }

    // ── Read-time neighbour expansion ──

    private static StoredConversation SixMessages() => Conversation(
        Msg("user", "one"), Msg("assistant", "two"), Msg("user", "three"),
        Msg("assistant", "four"), Msg("user", "five"), Msg("assistant", "six"));

    [Fact]
    public void Expand_IncludesTheNeighboursEitherSideOfAMatch()
    {
        var chunker = Chunker(ChunkingStrategy.Message, neighbourWindow: 1);

        var expanded = chunker.Expand(SixMessages(), [2]);

        Assert.Equal([1, 2, 3], expanded.Select(c => c.Ordinal));
    }

    [Fact]
    public void Expand_WithWindowZero_ReturnsOnlyTheMatchedChunks()
    {
        var chunker = Chunker(ChunkingStrategy.Message, neighbourWindow: 0);

        var expanded = chunker.Expand(SixMessages(), [1, 4]);

        Assert.Equal([1, 4], expanded.Select(c => c.Ordinal));
    }

    [Fact]
    public void Expand_OverlappingWindows_IncludeEachChunkOnce_InDocumentOrder()
    {
        var chunker = Chunker(ChunkingStrategy.Message, neighbourWindow: 1);

        // Matches at 2 and 3 have windows 1-3 and 2-4, which overlap at 2 and 3.
        var expanded = chunker.Expand(SixMessages(), [3, 2]);

        Assert.Equal([1, 2, 3, 4], expanded.Select(c => c.Ordinal));
    }

    [Fact]
    public void Expand_ClampsToTheEndsOfTheConversation()
    {
        var chunker = Chunker(ChunkingStrategy.Message, neighbourWindow: 2);

        Assert.Equal([0, 1, 2], chunker.Expand(SixMessages(), [0]).Select(c => c.Ordinal));
        Assert.Equal([3, 4, 5], chunker.Expand(SixMessages(), [5]).Select(c => c.Ordinal));
    }

    [Fact]
    public void Expand_IgnoresAnOrdinalThatNoLongerExists()
    {
        // A conversation that has changed since it was embedded can be hit on an ordinal past its end.
        // Including a neighbour of it would be including an unrelated chunk.
        var chunker = Chunker(ChunkingStrategy.Message, neighbourWindow: 1);

        var expanded = chunker.Expand(SixMessages(), [99, -1]);

        Assert.Empty(expanded);
    }

    [Fact]
    public void Expand_UnderWholeConversation_IsTheConversation()
    {
        var chunker = Chunker(ChunkingStrategy.WholeConversation, neighbourWindow: 3);

        var chunk = Assert.Single(chunker.Expand(SixMessages(), [0]));

        Assert.Equal(ChunkingStrategy.WholeConversation, chunk.Strategy);
    }
}
