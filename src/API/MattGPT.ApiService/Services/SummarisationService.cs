using System.Text;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Result of a summarisation run.
/// </summary>
public record SummarisationResult(int Summarised, int Errors, int Skipped);

/// <summary>Outcome of generating one conversation's digest.</summary>
public enum DigestOutcome
{
    /// <summary>A digest was generated and stored.</summary>
    Generated,

    /// <summary>The conversation has no visible content, so there is nothing to summarise. Recorded as <see cref="ConversationSummaryStatus.Skipped"/>.</summary>
    Skipped,

    /// <summary>Generation failed. Recorded as <see cref="ConversationSummaryStatus.Failed"/>; the conversation is otherwise unchanged.</summary>
    Failed,
}

/// <summary>
/// Generates the two LLM artifacts for a conversation:
/// <list type="bullet">
/// <item>the <b>digest</b> (<see cref="StoredConversation.Summary"/>): 3–6 sentences, part of the embedding text, for retrieval;</item>
/// <item>the <b>record</b> (<see cref="StoredConversation.Record"/>): a human-readable account, generated on demand for export, never embedded.</item>
/// </list>
/// Every caller (the bulk run, import, the export button, chat sessions) goes through the
/// single-conversation methods here, so there is one implementation of each.
/// </summary>
public class SummarisationService(
    IConversationRepository repository,
    IChatClient chatClient,
    ILogger<SummarisationService> logger,
    EmbeddingService? embedder = null)
{
    /// <summary>Maximum characters in a single LLM prompt, for both the digest and the record.</summary>
    internal const int MaxPromptChars = 12_000;

    /// <summary>
    /// A single message may use at most this fraction (1/n) of the transcript budget. A longer one is
    /// truncated, so one long paste can't crowd out the rest of the conversation.
    /// </summary>
    internal const int MessageShareDivisor = 4;

    /// <summary>Floor for the transcript budget, in case a very long title leaves almost no room.</summary>
    private const int MinTranscriptChars = 1_000;

    /// <summary>Number of conversations to load per batch.</summary>
    private const int BatchSize = 50;

    internal const string TruncatedMessageSuffix = " [… message truncated …]";

    private const string DigestFooter = """

        Write a concise summary (3–6 sentences) that captures:
        - The main topic, project, or problem discussed
        - Key decisions, conclusions, or outputs
        - Any notable context (code written, files mentioned, images generated, tools used)

        Summary:
        """;

    private const string RecordFooter = """

        Write a record of this conversation in Markdown, for the user to read later. It must make sense on its own. Cover:
        - The topics discussed, in the order they came up
        - Decisions made, including any that were later changed or reversed, and what replaced them
        - Outputs produced (code, documents, plans, images) and what they were for
        - Questions left open
        Keep the length proportional to the conversation: a short exchange needs a few lines, a long working session needs more.
        Use short headings and bullet points. Do not invent anything that is not in the conversation. Do not add a title; one is added for you.

        Record:
        """;

    /// <summary>
    /// Generates a digest for every imported conversation that doesn't have one (including earlier
    /// failures, but not conversations skipped for having no content), and embeds each one straight
    /// after so its digest is searchable.
    /// </summary>
    /// <returns>A <see cref="SummarisationResult"/> with counts of successes, failures and skips.</returns>
    public async Task<SummarisationResult> SummariseAsync(CancellationToken ct = default)
    {
        int summarised = 0;
        int errors = 0;
        int skipped = 0;

        // A failed digest stays in the unsummarised set; exclude what has been attempted so each
        // conversation is tried at most once per run.
        var attempted = new HashSet<string>();

        while (!ct.IsCancellationRequested)
        {
            var batch = await repository.GetUnsummarisedAsync(BatchSize, attempted, ct);

            if (batch.Count == 0)
                break;

            logger.LogInformation("Summarisation batch: {Count} conversations to process.", batch.Count);

            foreach (var conversation in batch)
            {
                if (ct.IsCancellationRequested)
                    break;

                attempted.Add(conversation.ConversationId);

                switch (await SummariseConversationAsync(conversation, embedAfter: true, ct))
                {
                    case DigestOutcome.Generated: summarised++; break;
                    case DigestOutcome.Failed:    errors++;     break;
                    case DigestOutcome.Skipped:   skipped++;    break;
                }
            }
        }

        logger.LogInformation(
            "Summarisation complete: {Summarised} summarised, {Errors} errors, {Skipped} skipped.",
            summarised, errors, skipped);

        return new SummarisationResult(summarised, errors, skipped);
    }

    /// <summary>Loads a conversation and generates its digest; see <see cref="SummariseConversationAsync(StoredConversation, bool, CancellationToken)"/>.</summary>
    public async Task<DigestOutcome> SummariseConversationAsync(string conversationId, bool embedAfter, CancellationToken ct = default)
    {
        var conversation = await repository.GetByIdAsync(conversationId, ct);
        if (conversation is null)
        {
            logger.LogWarning("Cannot summarise conversation {Id}: not found.", conversationId);
            return DigestOutcome.Failed;
        }

        return await SummariseConversationAsync(conversation, embedAfter, ct);
    }

    /// <summary>
    /// Generates and stores one conversation's digest. A generated digest marks the conversation
    /// <see cref="ConversationProcessingStatus.Summarised"/> (digest not yet in its embedding). With
    /// <paramref name="embedAfter"/>, the conversation is then re-embedded straight away, so a digest
    /// generated after embedding becomes searchable without a manual embed run. Callers that embed in
    /// bulk afterwards (the import pipeline) pass false. Skips and failures leave the processing
    /// status alone, so they never block or undo embedding.
    /// </summary>
    public async Task<DigestOutcome> SummariseConversationAsync(
        StoredConversation conversation, bool embedAfter, CancellationToken ct = default)
    {
        string? digest;
        try
        {
            digest = await GenerateSummaryAsync(conversation, ct);

            if (digest is null)
            {
                logger.LogInformation(
                    "Conversation {Id} ({Title}) has no visible content to summarise; digest skipped.",
                    conversation.ConversationId, conversation.Title);

                await repository.UpdateSummaryAsync(
                    conversation.ConversationId, conversation.Summary, ConversationSummaryStatus.Skipped, status: null, ct);
                return DigestOutcome.Skipped;
            }

            if (string.IsNullOrWhiteSpace(digest))
                throw new InvalidOperationException("The model returned an empty summary.");

            await repository.UpdateSummaryAsync(
                conversation.ConversationId, digest, ConversationSummaryStatus.Generated,
                ConversationProcessingStatus.Summarised, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Failed to summarise conversation {Id} ({Title}).",
                conversation.ConversationId, conversation.Title);

            await TryMarkFailedAsync(conversation, ct);
            return DigestOutcome.Failed;
        }

        conversation.Summary = digest;
        conversation.SummaryStatus = ConversationSummaryStatus.Generated;
        conversation.ProcessingStatus = ConversationProcessingStatus.Summarised;

        logger.LogDebug("Summarised conversation {Id} ({Title}).", conversation.ConversationId, conversation.Title);

        if (embedAfter && embedder is not null)
            await embedder.EmbedOneAsync(conversation, ct);

        return DigestOutcome.Generated;
    }

    /// <summary>
    /// Generates a digest for one conversation without storing it, from the record when there is one
    /// and from the messages otherwise. Returns null when there is no visible content to summarise.
    /// Exceptions from the LLM propagate.
    /// </summary>
    public async Task<string?> GenerateSummaryAsync(StoredConversation conversation, CancellationToken ct = default)
    {
        var prompt = string.IsNullOrWhiteSpace(conversation.Record)
            ? BuildPrompt(conversation)
            : BuildDigestFromRecordPrompt(conversation);
        if (prompt is null)
            return null;

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        return response.Text;
    }

    /// <summary>
    /// Generates and stores one conversation's record. Changes nothing else: the record is never
    /// embedded, so this cannot shift retrieval.
    /// </summary>
    /// <returns>The conversation with its new <see cref="StoredConversation.Record"/>.</returns>
    /// <exception cref="InvalidOperationException">The conversation doesn't exist, has nothing to record, or the model returned nothing.</exception>
    public async Task<StoredConversation> GenerateRecordAsync(string conversationId, CancellationToken ct = default)
    {
        var conversation = await repository.GetByIdAsync(conversationId, ct)
            ?? throw new InvalidOperationException($"Conversation '{conversationId}' was not found.");

        var prompt = BuildRecordPrompt(conversation)
            ?? throw new InvalidOperationException("The conversation has no visible content to record.");

        var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
        var record = response.Text;
        if (string.IsNullOrWhiteSpace(record))
            throw new InvalidOperationException("The model returned an empty record.");

        await repository.UpdateRecordAsync(conversationId, record.Trim(), ct);
        conversation.Record = record.Trim();

        logger.LogInformation("Generated record for conversation {Id} ({Chars} chars).", conversationId, conversation.Record.Length);
        return conversation;
    }

    private async Task TryMarkFailedAsync(StoredConversation conversation, CancellationToken ct)
    {
        try
        {
            await repository.UpdateSummaryAsync(
                conversation.ConversationId, conversation.Summary, ConversationSummaryStatus.Failed, status: null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record the failed digest for conversation {Id}.", conversation.ConversationId);
        }
    }

    /// <summary>
    /// Builds the digest prompt from a conversation's messages. Returns null only when there are no
    /// visible messages with content; see <see cref="BuildTranscript"/> for how long conversations are cut.
    /// </summary>
    public static string? BuildPrompt(StoredConversation conversation)
        => BuildMessagesPrompt(
            conversation.Source == ConversationSource.ChatSession
                ? "You are summarising a conversation between a user and an AI assistant to capture its key information for future reference."
                : "You are summarising a ChatGPT conversation to capture its key information for future reference.",
            conversation, DigestFooter);

    /// <summary>Builds the record prompt from a conversation's messages; null when there is nothing to record.</summary>
    public static string? BuildRecordPrompt(StoredConversation conversation)
        => BuildMessagesPrompt(
            conversation.Source == ConversationSource.ChatSession
                ? "You are writing a record of a conversation between a user and an AI assistant."
                : "You are writing a record of a ChatGPT conversation.",
            conversation, RecordFooter);

    /// <summary>
    /// Builds the digest prompt from a conversation's record. The record has already surfaced the
    /// decisions and outcomes, so this is a short input that needs no head-and-tail cutting.
    /// </summary>
    public static string BuildDigestFromRecordPrompt(StoredConversation conversation)
    {
        var header = new StringBuilder()
            .AppendLine("You are summarising a record of a conversation to capture its key information for future reference.")
            .AppendLine()
            .Append("Conversation title: ").AppendLine(conversation.Title ?? "(untitled)")
            .AppendLine()
            .AppendLine("Conversation record:")
            .ToString();

        var budget = Math.Max(MinTranscriptChars, MaxPromptChars - header.Length - DigestFooter.Length);
        var record = conversation.Record!.Trim();
        if (record.Length > budget)
            record = record[..(budget - TruncatedMessageSuffix.Length)] + TruncatedMessageSuffix;

        return header + record + "\n" + DigestFooter;
    }

    private static string? BuildMessagesPrompt(string intro, StoredConversation conversation, string footer)
    {
        var header = new StringBuilder()
            .AppendLine(intro)
            .AppendLine()
            .Append("Conversation title: ").AppendLine(conversation.Title ?? "(untitled)");
        if (!string.IsNullOrEmpty(conversation.DefaultModelSlug))
            header.Append("Model used: ").AppendLine(conversation.DefaultModelSlug);
        header.AppendLine().AppendLine("Messages:");

        // The footer's real length is reserved, so editing the instructions can't overrun the limit.
        var budget = Math.Max(MinTranscriptChars, MaxPromptChars - header.Length - footer.Length);
        var transcript = BuildTranscript(conversation.LinearisedMessages, budget);

        return transcript is null ? null : header + transcript + footer;
    }

    /// <summary>
    /// Renders the visible messages as <c>role: content</c> lines within <paramref name="budget"/>
    /// characters, or returns null when there are no visible messages with content.
    /// </summary>
    /// <remarks>
    /// When the whole conversation doesn't fit:
    /// <list type="number">
    /// <item>Any single message longer than 1/<see cref="MessageShareDivisor"/> of the budget is cut
    /// to that length. One long paste (including as the first message) then can't use the whole
    /// budget and stop the rest from being collected.</item>
    /// <item>If it still doesn't fit, the opening and closing are kept and the middle is replaced by
    /// a marker. The ending is where decisions and conclusions usually are, so dropping the tail
    /// (as a head-only cut would) loses the most important part.</item>
    /// </list>
    /// </remarks>
    internal static string? BuildTranscript(IEnumerable<StoredMessage> messages, int budget)
    {
        // Skip hidden and zero-weight messages (system scaffolding, custom instructions), and empty
        // ones, to focus on actual conversational content.
        var lines = messages
            .Where(m => !m.IsHidden && m.Weight != 0.0)
            .Select(m => (m.Role, Content: string.Join(" ", m.Parts)))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => $"{m.Role}: {m.Content}\n")
            .ToList();

        if (lines.Count == 0)
            return null;

        if (lines.Sum(l => l.Length) <= budget)
            return string.Concat(lines);

        var cap = budget / MessageShareDivisor;
        lines = [.. lines.Select(l => CapLine(l, cap))];

        if (lines.Sum(l => l.Length) <= budget)
            return string.Concat(lines);

        // Reserve room for the marker at its longest (every message omitted).
        var available = budget - MiddleMarker(lines.Count).Length;

        var head = 0;
        var used = 0;
        while (head < lines.Count && used + lines[head].Length <= available / 2)
            used += lines[head++].Length;

        var tail = lines.Count;
        while (tail > head && used + lines[tail - 1].Length <= available)
            used += lines[--tail].Length;

        var sb = new StringBuilder();
        for (var i = 0; i < head; i++)
            sb.Append(lines[i]);
        if (tail > head)
            sb.Append(MiddleMarker(tail - head));
        for (var i = tail; i < lines.Count; i++)
            sb.Append(lines[i]);

        return sb.ToString();
    }

    private static string CapLine(string line, int cap)
        => line.Length <= cap
            ? line
            : line[..Math.Max(0, cap - TruncatedMessageSuffix.Length - 1)] + TruncatedMessageSuffix + "\n";

    internal static string MiddleMarker(int omitted)
        => $"[... {omitted} message(s) from the middle of the conversation omitted ...]\n";
}
