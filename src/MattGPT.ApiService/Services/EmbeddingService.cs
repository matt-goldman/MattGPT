using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Result of an embedding generation run.
/// </summary>
public record EmbeddingResult(int Embedded, int Errors, int Skipped);

/// <summary>
/// Generates embedding vectors for conversations that have been summarised but not yet embedded.
/// Processes conversations in batches and stores the embedding on the MongoDB document.
/// </summary>
public class EmbeddingService
{
    /// <summary>Number of conversations to load per batch from MongoDB.</summary>
    private const int BatchSize = 50;

    private readonly IConversationRepository _repository;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IConversationRepository repository,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<EmbeddingService> logger)
    {
        _repository = repository;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Processes all conversations with <see cref="ConversationProcessingStatus.Summarised"/> status,
    /// generates an embedding vector for each summary, and updates MongoDB.
    /// </summary>
    /// <returns>An <see cref="EmbeddingResult"/> with counts of successes and errors.</returns>
    public async Task<EmbeddingResult> EmbedAsync(CancellationToken ct = default)
    {
        int embedded = 0;
        int errors = 0;
        int skipped = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _repository.GetByStatusAsync(
                ConversationProcessingStatus.Summarised, BatchSize, ct);

            if (batch.Count == 0)
                break;

            _logger.LogInformation("Embedding batch: {Count} conversations to process.", batch.Count);

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
            }
        }

        _logger.LogInformation(
            "Embedding complete: {Embedded} embedded, {Errors} errors, {Skipped} skipped.",
            embedded, errors, skipped);

        return new EmbeddingResult(embedded, errors, skipped);
    }

    private enum EmbedOutcome { Success, Error, Skipped }

    private async Task<EmbedOutcome> EmbedConversationAsync(
        StoredConversation conversation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conversation.Summary))
        {
            _logger.LogDebug(
                "Conversation {Id} has no summary; marking as Embedded with null vector.",
                conversation.ConversationId);

            await _repository.UpdateEmbeddingAsync(
                conversation.ConversationId,
                embedding: null,
                ConversationProcessingStatus.Embedded,
                ct);
            return EmbedOutcome.Skipped;
        }

        try
        {
            var result = await _embeddingGenerator.GenerateAsync(
                [conversation.Summary], cancellationToken: ct);

            var vector = result[0].Vector.ToArray();

            await _repository.UpdateEmbeddingAsync(
                conversation.ConversationId,
                vector,
                ConversationProcessingStatus.Embedded,
                ct);

            _logger.LogDebug(
                "Embedded conversation {Id} ({Title}), dimensions: {Dims}.",
                conversation.ConversationId, conversation.Title, vector.Length);

            return EmbedOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to embed conversation {Id} ({Title}); marking as EmbeddingError.",
                conversation.ConversationId, conversation.Title);

            await TryMarkErrorAsync(conversation.ConversationId, ct);
            return EmbedOutcome.Error;
        }
    }

    private async Task TryMarkErrorAsync(string conversationId, CancellationToken ct)
    {
        try
        {
            await _repository.UpdateEmbeddingAsync(
                conversationId,
                embedding: null,
                ConversationProcessingStatus.EmbeddingError,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update EmbeddingError status for conversation {Id}.", conversationId);
        }
    }
}
