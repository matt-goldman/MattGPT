using System.Text;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
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
    TimeProvider timeProvider,
    ILogger<EmbeddingService> logger)
{
    /// <summary>Number of conversations to load per batch from MongoDB.</summary>
    private const int BatchSize = 50;

    /// <summary>Maximum characters of conversation content to include for embedding.</summary>
    internal const int MaxEmbeddingTextChars = 8_000;

    /// <summary>
    /// Fallback chunk size (in characters) used when the embedding model rejects the full
    /// text because it exceeds the model's context window. Conservative enough (~500 tokens)
    /// to fit most embedding models.
    /// </summary>
    internal const int FallbackChunkChars = 2_000;

    /// <summary>Maximum number of retry attempts for transient embedding failures.</summary>
    internal const int MaxRetries = 3;

    /// <summary>Base delay (in seconds) for exponential backoff between retries.</summary>
    internal const int BaseDelaySeconds = 2;

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
            var vector = await GenerateChunkedEmbeddingAsync(embeddingText, ct);

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

            // Include citation context (file names and web source titles/URLs).
            if (msg.Citations is { Count: > 0 })
            {
                foreach (var citation in msg.Citations)
                {
                    var citName = !string.IsNullOrWhiteSpace(citation.Name) ? citation.Name
                        : citation.Source;
                    if (citName is not null)
                    {
                        var citLine = $"[Cited: {citName}]\n";
                        if (sb.Length + citLine.Length <= MaxEmbeddingTextChars)
                            sb.Append(citLine);
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Generates an embedding for <paramref name="text"/>. Tries to embed the full text
    /// first; if the model reports a context-length error, falls back to chunking the text
    /// into <see cref="FallbackChunkChars"/>-sized pieces and averaging the resulting vectors.
    /// This keeps quality high for models with large context windows while still working
    /// with smaller models. Transient failures are retried with exponential backoff.
    /// </summary>
    private async Task<float[]> GenerateChunkedEmbeddingAsync(string text, CancellationToken ct)
    {
        // Fast path — try the full text first.
        try
        {
            var result = await GenerateWithRetryAsync(text, ct);
            return result[0].Vector.ToArray();
        }
        catch (Exception ex) when (IsContextLengthError(ex))
        {
            logger.LogDebug(
                "Embedding model rejected full text ({Len} chars); falling back to chunked embedding.",
                text.Length);
        }

        // Slow path — chunk, embed each piece, and average.
        var chunks = ChunkText(text, FallbackChunkChars);
        logger.LogDebug(
            "Splitting text ({Len} chars) into {Chunks} chunks of ~{ChunkSize} chars.",
            text.Length, chunks.Count, FallbackChunkChars);

        float[]? averaged = null;

        foreach (var chunk in chunks)
        {
            var result = await GenerateWithRetryAsync(chunk, ct);
            var vec = result[0].Vector.ToArray();

            if (averaged is null)
            {
                averaged = new float[vec.Length];
            }

            for (int i = 0; i < vec.Length; i++)
                averaged[i] += vec[i];
        }

        // Average and L2-normalise so similarity searches behave consistently.
        if (averaged is not null)
        {
            float norm = 0f;
            for (int i = 0; i < averaged.Length; i++)
            {
                averaged[i] /= chunks.Count;
                norm += averaged[i] * averaged[i];
            }

            norm = MathF.Sqrt(norm);
            if (norm > 0f)
            {
                for (int i = 0; i < averaged.Length; i++)
                    averaged[i] /= norm;
            }
        }

        return averaged ?? [];
    }

    /// <summary>
    /// Wraps <see cref="IEmbeddingGenerator{TInput,TEmbedding}.GenerateAsync"/> with
    /// exponential-backoff retry for transient failures (HTTP errors, timeouts).
    /// Context-length errors are never retried — they are rethrown immediately so the
    /// caller can fall back to chunking.
    /// </summary>
    private async Task<GeneratedEmbeddings<Embedding<float>>> GenerateWithRetryAsync(
        string text, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                                       && !IsContextLengthError(ex)
                                       && IsTransientError(ex)
                                       && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(BaseDelaySeconds * Math.Pow(2, attempt));
                logger.LogWarning(
                    ex,
                    "Transient embedding failure (attempt {Attempt}/{MaxRetries}); retrying in {Delay}s.",
                    attempt + 1, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the exception (or an inner exception) looks like a
    /// transient/retryable failure — HTTP errors, I/O errors, or timeouts — as opposed
    /// to a permanent model-level rejection (context length, bad input, etc.).
    /// </summary>
    internal static bool IsTransientError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException)
                return true;

            if (current is IOException)
                return true;

            // TaskCanceledException wraps HttpClient timeouts but should NOT be treated
            // as transient when it comes from an explicit cancellation token.
            if (current is TaskCanceledException tce && tce.CancellationToken == default)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the exception (or an inner exception) indicates the
    /// embedding model rejected the input because it exceeded the context length.
    /// Checks both the Ollama-specific exception type and common error message patterns
    /// so this works across providers.
    /// </summary>
    private static bool IsContextLengthError(Exception ex)
    {
        // Walk the exception chain — some providers wrap the real error.
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var msg = current.Message;
            if (string.IsNullOrEmpty(msg))
                continue;

            // Ollama: "the input length exceeds the context length"
            // OpenAI / Azure OpenAI: "maximum context length", "too many tokens"
            if (msg.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("too many tokens", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("token limit", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("input is too long", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into chunks of at most <paramref name="maxChars"/>,
    /// breaking on the last newline within each window when possible.
    /// </summary>
    internal static List<string> ChunkText(string text, int maxChars)
    {
        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + maxChars, text.Length);

            // Try to break on a newline boundary to avoid splitting mid-sentence.
            if (end < text.Length)
            {
                int newline = text.LastIndexOf('\n', end - 1, end - start);
                if (newline > start)
                    end = newline + 1; // include the newline in this chunk
            }

            chunks.Add(text[start..end]);
            start = end;
        }

        return chunks;
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
