using System.Net;
using System.Text;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

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
/// Generates embedding vectors for imported conversations and stores them in the configured vector store.
/// Embeds directly from conversation content (title + messages), so LLM summarisation is
/// NOT required before embedding. Conversations with a summary use the summary as part of
/// the embedding text for better quality; conversations without one are still embedded.
/// </summary>
public class EmbeddingService(
    IConversationRepository repository,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IVectorStore vectorStore,
    TimeProvider timeProvider,
    IOptions<RagOptions> ragOptions,
    ILogger<EmbeddingService> logger)
{
    /// <summary>
    /// Provider/endpoint/model metadata for the configured embedding generator, captured once so
    /// failures can be logged with the destination (provider name, endpoint URI, model) the request
    /// was sent to. Null when the generator does not expose <see cref="EmbeddingGeneratorMetadata"/>.
    /// </summary>
    private readonly EmbeddingGeneratorMetadata? generatorMetadata =
        embeddingGenerator.GetService(typeof(EmbeddingGeneratorMetadata)) as EmbeddingGeneratorMetadata;

    /// <summary>Number of conversations to load per batch from MongoDB.</summary>
    private const int BatchSize = 50;

    /// <summary>
    /// Maximum characters of a server error body to include in a diagnostic log line. Provider
    /// error responses are small JSON documents, but cap them so a pathological response cannot
    /// flood the logs.
    /// </summary>
    private const int MaxServerErrorChars = 2_000;

    /// <summary>
    /// Standard maximum characters of conversation content to include for embedding. Also the size the
    /// text is trimmed to when a longer text is rejected for exceeding the model's context window.
    /// </summary>
    internal const int MaxEmbeddingTextChars = 8_000;

    /// <summary>
    /// Default maximum characters of conversation content to include for embedding when
    /// <see cref="RagOptions.UseReranking"/> is enabled, which is paired with a long-context
    /// embedding model (~8k tokens).
    /// </summary>
    internal const int LongContextMaxEmbeddingTextChars = 32_000;

    /// <summary>
    /// Effective maximum embedding text length: <see cref="RagOptions.MaxEmbeddingChars"/> when set,
    /// otherwise <see cref="LongContextMaxEmbeddingTextChars"/> with reranking or
    /// <see cref="MaxEmbeddingTextChars"/> without.
    /// </summary>
    private readonly int maxEmbeddingChars = ragOptions.Value.MaxEmbeddingChars
        ?? (ragOptions.Value.UseReranking ? LongContextMaxEmbeddingTextChars : MaxEmbeddingTextChars);

    /// <summary>
    /// Fallback chunk size (in characters) used when the embedding model rejects the full
    /// text because it exceeds the model's context window. Conservative enough (~500 tokens)
    /// to fit most embedding models.
    /// </summary>
    internal const int FallbackChunkChars = 2_000;

    /// <summary>
    /// Statuses eligible for embedding: freshly imported and summarised conversations, plus
    /// conversations whose embedding previously failed (<see cref="ConversationProcessingStatus.EmbeddingError"/>)
    /// so a re-run retries them. Note that failures are re-marked <c>EmbeddingError</c>, so within a
    /// single run each conversation is attempted at most once (see the <c>attempted</c> set in
    /// <see cref="EmbedAsync"/>) to avoid re-fetching a persistently-failing conversation forever.
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>Base delay (in seconds) for exponential backoff between retries.</summary>
    private const int BaseDelaySeconds = 2;

    /// <summary>Statuses eligible for embedding — both freshly imported and summarised conversations.</summary>
    private static readonly ConversationProcessingStatus[] EmbeddableStatuses =
    [
        ConversationProcessingStatus.Imported,
        ConversationProcessingStatus.Summarised,
        ConversationProcessingStatus.EmbeddingError,
    ];

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
        var embedded = 0;
        var errors = 0;
        var skipped = 0;

        // Failed embeddings are re-marked EmbeddingError, which keeps them in the eligible set.
        // Track the conversations we've already attempted this run and exclude them from the next
        // fetch, so each conversation is attempted at most once — otherwise a persistent failure
        // would be re-fetched batch after batch and spin this loop forever.
        var attempted = new HashSet<string>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await repository.GetByStatusesAsync(EmbeddableStatuses, BatchSize, attempted, ct);

            if (batch.Count == 0)
            {
                if (attempted.Count == 0)
                    logger.LogInformation("No embeddable conversations found in repository.");
                break;
            }

            logger.LogInformation("Embedding batch: {Count} conversations to process.", batch.Count);

            foreach (var conversation in batch)
            {
                if (ct.IsCancellationRequested)
                    break;

                attempted.Add(conversation.ConversationId);

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

        if (ct.IsCancellationRequested)
        {
            logger.LogInformation("Embedding cancelled");
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
        var embeddingText = BuildEmbeddingText(conversation, maxEmbeddingChars);

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
            var vector = await GenerateChunkedEmbeddingAsync(conversation, embeddingText, ct);

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
            LogEmbeddingFailure(ex, conversation, embeddingText.Length);

            await TryMarkErrorAsync(conversation.ConversationId, ct);
            return EmbedOutcome.Error;
        }
    }

    /// <summary>
    /// Builds the text that will be embedded. Uses the summary if available (higher quality),
    /// but always includes the title and message content so that freshly imported conversations
    /// can be embedded without waiting for LLM summarisation. Message content is included up to
    /// <paramref name="maxChars"/> characters.
    /// </summary>
    internal static string BuildEmbeddingText(StoredConversation conversation, int maxChars = MaxEmbeddingTextChars)
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

            if (sb.Length + line.Length > maxChars)
            {
                // Fit as much as we can.
                var remaining = maxChars - sb.Length;
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
                        if (sb.Length + citLine.Length <= maxChars)
                            sb.Append(citLine);
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Generates an embedding for <paramref name="text"/> (built from <paramref name="conversation"/>).
    /// Tries to embed the full text first; if the model reports a context-length error and the text
    /// is longer than <see cref="MaxEmbeddingTextChars"/>, retries with the conversation rebuilt to
    /// that length. If that is still too long, falls back to chunking the (trimmed) text into
    /// <see cref="FallbackChunkChars"/>-sized pieces and averaging the resulting vectors.
    /// This keeps quality high for models with large context windows while still working
    /// with smaller models, and bounds the number of chunk requests. Transient failures are
    /// retried with exponential backoff.
    /// </summary>
    private async Task<float[]> GenerateChunkedEmbeddingAsync(
        StoredConversation conversation, string text, CancellationToken ct)
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
                "Embedding model rejected full text ({Len} chars); falling back to shorter text.",
                text.Length);
        }

        // Trim path — long-context text was too long for the model; retry at the standard length,
        // trimmed on message boundaries. Chunking below then works from the trimmed text, so a very
        // long conversation can't fan out into hundreds of chunk requests.
        if (text.Length > MaxEmbeddingTextChars)
        {
            text = BuildEmbeddingText(conversation, MaxEmbeddingTextChars);

            try
            {
                var result = await GenerateWithRetryAsync(text, ct);
                return result[0].Vector.ToArray();
            }
            catch (Exception ex) when (IsContextLengthError(ex))
            {
                logger.LogDebug(
                    "Embedding model rejected trimmed text ({Len} chars); falling back to chunked embedding.",
                    text.Length);
            }
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

            averaged ??= new float[vec.Length];

            for (var i = 0; i < vec.Length; i++)
                averaged[i] += vec[i];
        }

        // Average and L2-normalise so similarity searches behave consistently.
        if (averaged is null) return averaged ?? [];
        {
            var norm = 0f;
            for (var i = 0; i < averaged.Length; i++)
            {
                averaged[i] /= chunks.Count;
                norm += averaged[i] * averaged[i];
            }

            norm = MathF.Sqrt(norm);
            if (!(norm > 0f)) return averaged;
            
            for (var i = 0; i < averaged.Length; i++)
                averaged[i] /= norm;
        }

        return averaged;
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
        for (var attempt = 0; ; attempt++)
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
                var status = GetHttpStatusCode(ex);
                TryGetServerErrorBody(ex, out var serverError);
                logger.LogWarning(
                    ex,
                    "Transient embedding failure (attempt {Attempt}/{MaxRetries}); HTTP status: {Status}; "
                    + "server error: {ServerError}; endpoint: {Endpoint}; model: {Model}. Retrying in {Delay}s.",
                    attempt + 1,
                    MaxRetries,
                    status == 0 ? "(none)" : status,
                    serverError ?? "(none)",
                    generatorMetadata?.ProviderUri?.ToString() ?? "(unknown)",
                    generatorMetadata?.DefaultModelId ?? "(unknown)",
                    delay.TotalSeconds);
                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the exception (or an inner exception) looks like a
    /// <em>transient</em> failure worth retrying — a connection-level network error, an I/O
    /// error, a timeout, or an HTTP response whose status a retry could plausibly clear
    /// (see <see cref="IsRetryableStatus"/>). Permanent rejections — authentication (401),
    /// authorisation (403), bad request (400), not found (404) and the other 4xx — return
    /// <c>false</c>: an identical retry cannot fix them, so retrying only wastes time and quota.
    /// Anything unrecognised is treated as non-transient (the conservative default).
    /// </summary>
    /// <remarks>
    /// This service is provider-agnostic, so it must classify failures from every embedding
    /// backend. The BCL <see cref="HttpRequestException"/> is matched directly; provider SDKs
    /// (OpenAI / Azure OpenAI via System.ClientModel, Azure via Azure.Core) instead throw their
    /// own exception types that carry the HTTP status on an <c>int Status</c> property, which
    /// <see cref="TryGetHttpStatusCode"/> reads reflectively so this assembly need not reference
    /// each provider SDK.
    /// </remarks>
    internal static bool IsTransientError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException httpEx:
                    // No status => the request never got an HTTP response (DNS failure,
                    // connection refused/reset): a classic transient network fault.
                    return httpEx.StatusCode is not { } status || IsRetryableStatus((int)status);

                case IOException:
                // TaskCanceledException wraps HttpClient timeouts but should NOT be treated
                // as transient when it comes from an explicit cancellation token.
                case TaskCanceledException tce when tce.CancellationToken == CancellationToken.None:
                    return true;
            }

            // Provider SDK exceptions that are not HttpRequestException but expose an HTTP status.
            if (TryGetHttpStatusCode(current, out var sdkStatus))
                return IsRetryableStatus(sdkStatus);
        }

        return false;
    }

    /// <summary>
    /// Classifies an HTTP status code as retryable. Only timeouts (408), rate limiting (429)
    /// and the transient server-side faults (500, 502, 503, 504) are retried; every other
    /// status — including all other 4xx — is permanent for an identical request.
    /// </summary>
    internal static bool IsRetryableStatus(int statusCode) => statusCode switch
    {
        408 => true,                      // Request Timeout
        429 => true,                      // Too Many Requests (rate limited / quota)
        500 or 502 or 503 or 504 => true, // server faults and gateway errors
        _ => false,
    };

    /// <summary>
    /// Attempts to read an HTTP status code from a provider SDK exception that is not an
    /// <see cref="HttpRequestException"/> but exposes a public <c>int Status</c> property — e.g.
    /// System.ClientModel's <c>ClientResultException</c> (OpenAI / Azure OpenAI) or Azure.Core's
    /// <c>RequestFailedException</c>. Read reflectively so this provider-agnostic service need
    /// not take a reference on each provider SDK.
    /// </summary>
    private static bool TryGetHttpStatusCode(Exception ex, out int statusCode)
    {
        if (ex.GetType().GetProperty("Status", typeof(int))?.GetValue(ex) is int status and > 0)
        {
            statusCode = status;
            return true;
        }

        statusCode = 0;
        return false;
    }

    /// <summary>
    /// Walks the exception chain and returns the first HTTP status code it finds — from a BCL
    /// <see cref="HttpRequestException"/> or, reflectively, from a provider SDK exception's
    /// <c>int Status</c> property (see <see cref="TryGetHttpStatusCode"/>). Returns <c>0</c> when
    /// no HTTP status is present (e.g. a connection-level failure that never got a response).
    /// </summary>
    internal static int GetHttpStatusCode(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } code })
                return (int)code;

            if (TryGetHttpStatusCode(current, out var status))
                return status;
        }

        return 0;
    }

    /// <summary>
    /// Attempts to extract the raw error body the server returned — the most useful field for
    /// diagnosing a 400, since it carries the provider's own explanation (an unknown model, an
    /// input that is too long, a bad dimension, an invalid API key, and so on). Walks the
    /// exception chain and reflectively invokes <c>GetRawResponse()</c> — exposed by System.ClientModel's
    /// <c>ClientResultException</c> (OpenAI / Azure OpenAI) and Azure.Core's <c>RequestFailedException</c> —
    /// then reads the response's <c>Content</c>. Read reflectively so this provider-agnostic service
    /// need not reference each provider SDK. The body is truncated to <see cref="MaxServerErrorChars"/>.
    /// </summary>
    internal static bool TryGetServerErrorBody(Exception ex, out string? body)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var response = current.GetType()
                .GetMethod("GetRawResponse", Type.EmptyTypes)?
                .Invoke(current, null);

            var content = response?.GetType().GetProperty("Content")?.GetValue(response)?.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                body = content!.Length > MaxServerErrorChars
                    ? string.Concat(content.AsSpan(0, MaxServerErrorChars), "…(truncated)")
                    : content;
                return true;
            }
        }

        body = null;
        return false;
    }

    /// <summary>
    /// Logs a rich diagnostic record for a failed embedding: the HTTP status, the raw error body
    /// returned by the server, and the destination the request was sent to (provider, endpoint,
    /// model), plus the size of the input. The conversation text itself is never logged — only its
    /// length — to keep conversation content out of the logs.
    /// </summary>
    private void LogEmbeddingFailure(Exception ex, StoredConversation conversation, int textLength)
    {
        var status = GetHttpStatusCode(ex);
        TryGetServerErrorBody(ex, out var serverError);

        logger.LogWarning(
            ex,
            "Failed to embed conversation {Id} ({Title}); marking as EmbeddingError. "
            + "HTTP status: {Status}; server error: {ServerError}; "
            + "provider: {Provider}; endpoint: {Endpoint}; model: {Model}; input chars: {TextLength}.",
            conversation.ConversationId,
            conversation.Title,
            status == 0 ? "(none)" : status,
            serverError ?? "(none)",
            generatorMetadata?.ProviderName ?? "(unknown)",
            generatorMetadata?.ProviderUri?.ToString() ?? "(unknown)",
            generatorMetadata?.DefaultModelId ?? "(unknown)",
            textLength);
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
        var start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + maxChars, text.Length);

            // Try to break on a newline boundary to avoid splitting mid-sentence.
            if (end < text.Length)
            {
                var newline = text.LastIndexOf('\n', end - 1, end - start);
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
