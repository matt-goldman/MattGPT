using System.Text;
using MattGPT.ApiService.Models;
using Microsoft.Extensions.AI;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Result of a summarisation run.
/// </summary>
public record SummarisationResult(int Summarised, int Errors, int Skipped);

/// <summary>
/// Generates LLM summaries for conversations that have been imported but not yet summarised.
/// Processes conversations in batches to avoid overwhelming the LLM endpoint.
/// </summary>
public class SummarisationService
{
    /// <summary>Maximum number of characters from linearised messages to include in a single LLM prompt.</summary>
    private const int MaxPromptChars = 12_000;

    /// <summary>Number of conversations to load per batch from MongoDB.</summary>
    private const int BatchSize = 50;

    private readonly IConversationRepository _repository;
    private readonly IChatClient _chatClient;
    private readonly ILogger<SummarisationService> _logger;

    public SummarisationService(
        IConversationRepository repository,
        IChatClient chatClient,
        ILogger<SummarisationService> logger)
    {
        _repository = repository;
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// Processes all conversations with <see cref="ConversationProcessingStatus.Imported"/> status,
    /// generates an LLM summary for each, and updates MongoDB.
    /// </summary>
    /// <returns>A <see cref="SummarisationResult"/> with counts of successes and errors.</returns>
    public async Task<SummarisationResult> SummariseAsync(CancellationToken ct = default)
    {
        int summarised = 0;
        int errors = 0;
        int skipped = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _repository.GetByStatusAsync(
                ConversationProcessingStatus.Imported, BatchSize, ct);

            if (batch.Count == 0)
                break;

            _logger.LogInformation("Summarisation batch: {Count} conversations to process.", batch.Count);

            foreach (var conversation in batch)
            {
                if (ct.IsCancellationRequested)
                    break;

                var result = await SummariseConversationAsync(conversation, ct);
                switch (result)
                {
                    case SummariseOutcome.Success: summarised++; break;
                    case SummariseOutcome.Error:   errors++;     break;
                    case SummariseOutcome.Skipped: skipped++;    break;
                }
            }
        }

        _logger.LogInformation(
            "Summarisation complete: {Summarised} summarised, {Errors} errors, {Skipped} skipped.",
            summarised, errors, skipped);

        return new SummarisationResult(summarised, errors, skipped);
    }

    private enum SummariseOutcome { Success, Error, Skipped }

    private async Task<SummariseOutcome> SummariseConversationAsync(
        StoredConversation conversation, CancellationToken ct)
    {
        if (conversation.LinearisedMessages.Count == 0)
        {
            _logger.LogDebug(
                "Conversation {Id} has no messages; marking as Summarised with empty summary.",
                conversation.ConversationId);

            await _repository.UpdateSummaryAsync(
                conversation.ConversationId,
                summary: null,
                ConversationProcessingStatus.Summarised,
                ct);
            return SummariseOutcome.Skipped;
        }

        try
        {
            var prompt = BuildPrompt(conversation);
            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            var summary = response.Text;

            await _repository.UpdateSummaryAsync(
                conversation.ConversationId,
                summary,
                ConversationProcessingStatus.Summarised,
                ct);

            _logger.LogDebug(
                "Summarised conversation {Id} ({Title}).",
                conversation.ConversationId, conversation.Title);

            return SummariseOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to summarise conversation {Id} ({Title}); marking as SummaryError.",
                conversation.ConversationId, conversation.Title);

            await TryMarkErrorAsync(conversation.ConversationId, ct);
            return SummariseOutcome.Error;
        }
    }

    private async Task TryMarkErrorAsync(string conversationId, CancellationToken ct)
    {
        try
        {
            await _repository.UpdateSummaryAsync(
                conversationId,
                summary: null,
                ConversationProcessingStatus.SummaryError,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update SummaryError status for conversation {Id}.", conversationId);
        }
    }

    public static string BuildPrompt(StoredConversation conversation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are summarising a ChatGPT conversation to capture its key information for future reference.");
        sb.AppendLine();
        sb.Append("Conversation title: ").AppendLine(conversation.Title ?? "(untitled)");
        if (!string.IsNullOrEmpty(conversation.DefaultModelSlug))
            sb.Append("Model used: ").AppendLine(conversation.DefaultModelSlug);
        sb.AppendLine();
        sb.AppendLine("Messages:");

        // Append messages, truncating when we approach the character limit.
        int headerLen = sb.Length;
        int remaining = MaxPromptChars - headerLen - 300; // reserve chars for the instruction footer
        bool truncated = false;

        foreach (var msg in conversation.LinearisedMessages)
        {
            var content = string.Join(" ", msg.Parts);
            var line = $"{msg.Role}: {content}\n";

            if (remaining - line.Length < 0)
            {
                truncated = true;
                break;
            }

            sb.Append(line);
            remaining -= line.Length;
        }

        if (truncated)
            sb.AppendLine("[... earlier messages truncated ...]");

        sb.AppendLine();
        sb.AppendLine("Write a concise summary (3–6 sentences) that captures:");
        sb.AppendLine("- The main topic, project, or problem discussed");
        sb.AppendLine("- Key decisions, conclusions, or outputs");
        sb.AppendLine("- Any notable context (code written, files mentioned, images generated, tools used)");
        sb.AppendLine();
        sb.Append("Summary:");

        return sb.ToString();
    }
}
