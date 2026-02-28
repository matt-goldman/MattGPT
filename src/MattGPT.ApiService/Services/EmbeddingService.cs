using System.Text;
using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Result of an embedding generation run.
/// </summary>
public record EmbeddingResult(int Embedded, int Errors, int Skipped);

/// <summary>
/// Progress snapshot reported after each conversation is processed during embedding.
/// </summary>
public record EmbeddingProgress(int Embedded, int Errors, int Skipped);

/// <summary>
/// Generates embedding vectors for imported conversations and stores them in Qdrant.
/// Embeds directly from conversation content (title + messages), so LLM summarisation is
/// NOT required before embedding. Conversations with a summary use the summary as part of
/// the embedding text for better quality; conversations without one are still embedded.
/// </summary>
public class EmbeddingService(
    IConversationRepository repository,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    ILogger<EmbeddingService> logger)
{
    /// <summary>Number of conversations to load per batch from MongoDB.</summary>
    private const int BatchSize = 50;

    /// <summary>Maximum characters of conversation content to include for embedding.</summary>
    internal const int MaxEmbeddingTextChars = 8_000;

    /// <summary>Statuses eligible for embedding — both freshly imported and summarised conversations.</summary>
    private static readonly ConversationProcessingStatus[] EmbeddableStatuses =
        [ConversationProcessingStatus.Imported, ConversationProcessingStatus.Summarised];

    /// <summary>
    /// Processes all conversations with <see cref="ConversationProcessingStatus.Imported"/> or
    /// <see cref="ConversationProcessingStatus.Summarised"/> status, generates an embedding
    /// vector from conversation content, and stores it in MongoDB and Qdrant.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="progress">Optional progress reporter, invoked after each conversation.</param>
    /// <returns>An <see cref="EmbeddingResult"/> with counts of successes and errors.</returns>
    public async Task<EmbeddingResult> EmbedAsync(CancellationToken ct = default, IProgress<EmbeddingProgress>? progress = null)
    {
        int embedded = 0;
        int errors = 0;
        int skipped = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await repository.GetByStatusesAsync(EmbeddableStatuses, BatchSize, ct);

            if (batch.Count == 0)
                break;

            logger.LogInformation("Embedding batch: {Count} conversations to process.", batch.Count);

            foreach (var conversation in batch)
            {
                if (ct.IsCancellationRequested)
                    break;

                var outcome = await EmbedConversationAsync(conversation, ct);
                switch (outcome)
                {
                    case EmbedOutcome.Success: embedded++; break;
                    case EmbedOutcome.Error:   errors++;   break;
                    case EmbedOutcome.Skipped: skipped++;  break;
                }

                progress?.Report(new EmbeddingProgress(embedded, errors, skipped));
            }
        }

        logger.LogInformation(
            "Embedding complete: {Embedded} embedded, {Errors} errors, {Skipped} skipped.",
            embedded, errors, skipped);

        return new EmbeddingResult(embedded, errors, skipped);
    }

    private enum EmbedOutcome { Success, Error, Skipped }

    private async Task<EmbedOutcome> EmbedConversationAsync(
        StoredConversation conversation, CancellationToken ct)
    {
        var embeddingText = BuildEmbeddingText(conversation);

        if (string.IsNullOrWhiteSpace(embeddingText))
        {
            logger.LogDebug(
                "Conversation {Id} has no embeddable content; marking as Embedded with null vector.",
                conversation.ConversationId);

            await repository.UpdateEmbeddingAsync(
                conversation.ConversationId,
                embedding: null,
                ConversationProcessingStatus.Embedded,
                ct);
            return EmbedOutcome.Skipped;
        }

        try
        {
            var result = await embeddingGenerator.GenerateAsync(
                [embeddingText], cancellationToken: ct);

            var vector = result[0].Vector.ToArray();

            await repository.UpdateEmbeddingAsync(
                conversation.ConversationId,
                vector,
                ConversationProcessingStatus.Embedded,
                ct);

            await TryUpsertVectorStoreAsync(conversation, vector, ct);

            logger.LogDebug(
                "Embedded conversation {Id} ({Title}), dimensions: {Dims}, text length: {TextLen}.",
                conversation.ConversationId, conversation.Title, vector.Length, embeddingText.Length);

            return EmbedOutcome.Success;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to embed conversation {Id} ({Title}); marking as EmbeddingError.",
                conversation.ConversationId, conversation.Title);

            await TryMarkErrorAsync(conversation.ConversationId, ct);
            return EmbedOutcome.Error;
        }
    }

    /// <summary>
    /// Builds the text that will be embedded. Uses the summary if available (higher quality),
    /// but always includes the title and message content so that freshly imported conversations
    /// can be embedded without waiting for LLM summarisation.
    /// </summary>
    internal static string BuildEmbeddingText(StoredConversation conversation)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(conversation.Title))
            sb.Append("Title: ").AppendLine(conversation.Title);

        if (!string.IsNullOrWhiteSpace(conversation.Summary))
            sb.Append("Summary: ").AppendLine(conversation.Summary);

        // Append message content up to the character limit.
        // Skip hidden and zero-weight messages (system scaffolding, custom instructions)
        // to dedicate the embedding window to actual conversational content.
        foreach (var msg in conversation.LinearisedMessages)
        {
            if (msg.IsHidden || msg.Weight == 0.0)
                continue;

            var content = string.Join(" ", msg.Parts);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var line = $"{msg.Role}: {content}\n";

            if (sb.Length + line.Length > MaxEmbeddingTextChars)
            {
                // Fit as much as we can.
                var remaining = MaxEmbeddingTextChars - sb.Length;
                if (remaining > 20)
                    sb.Append(line.AsSpan(0, remaining));
                break;
            }

            sb.Append(line);
        }

        return sb.ToString().Trim();
    }

    private async Task TryUpsertVectorStoreAsync(StoredConversation conversation, float[] vector, CancellationToken ct)
    {
        try
        {
            await vectorStore.UpsertAsync(conversation, vector, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to upsert conversation {Id} into vector store; the embedding is stored in MongoDB.",
                conversation.ConversationId);
        }
    }

    private async Task TryMarkErrorAsync(string conversationId, CancellationToken ct)
    {
        try
        {
            await repository.UpdateEmbeddingAsync(
                conversationId,
                embedding: null,
                ConversationProcessingStatus.EmbeddingError,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not update EmbeddingError status for conversation {Id}.", conversationId);
        }
    }
}
