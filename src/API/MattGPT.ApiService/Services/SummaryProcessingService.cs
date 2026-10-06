using System.Collections.Concurrent;
using System.Threading.Channels;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Services;

/// <summary>What a queued summarisation job does.</summary>
public enum SummaryJobKind
{
    /// <summary>Generate (and embed) one conversation's digest.</summary>
    Digest,

    /// <summary>Generate one conversation's record, for export.</summary>
    Record,

    /// <summary>Generate digests for every conversation that lacks one.</summary>
    Bulk,
}

/// <summary>A queued summarisation job. <paramref name="UserId"/> receives the completion notification.</summary>
public record SummaryJobRequest(SummaryJobKind Kind, string? ConversationId, string? UserId);

/// <summary>
/// Queue of background summarisation jobs, de-duplicated: a job for a conversation that is already
/// queued or running (same kind) is not queued again. Registered as a singleton.
/// </summary>
public class SummaryJobQueue
{
    private readonly Channel<SummaryJobRequest> _channel =
        Channel.CreateUnbounded<SummaryJobRequest>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    public ChannelReader<SummaryJobRequest> Reader => _channel.Reader;

    /// <summary>Queues <paramref name="request"/> unless an identical job is queued or running.</summary>
    /// <returns>Whether it was queued.</returns>
    public bool TryEnqueue(SummaryJobRequest request)
    {
        if (!_inFlight.TryAdd(Key(request.Kind, request.ConversationId), 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _inFlight.TryRemove(Key(request.Kind, request.ConversationId), out _);
        return false;
    }

    /// <summary>Whether a job of <paramref name="kind"/> for <paramref name="conversationId"/> is queued or running.</summary>
    public bool IsPending(SummaryJobKind kind, string? conversationId) => _inFlight.ContainsKey(Key(kind, conversationId));

    /// <summary>Called when a job finishes (either way), so the same job can be queued again.</summary>
    public void MarkDone(SummaryJobRequest request) => _inFlight.TryRemove(Key(request.Kind, request.ConversationId), out _);

    private static string Key(SummaryJobKind kind, string? conversationId) => $"{kind}:{conversationId}";
}

/// <summary>
/// Runs queued summarisation jobs (on-demand records and digests, and bulk digest runs) in the
/// background, and notifies the requesting user when each finishes.
/// </summary>
public class SummaryProcessingService(
    SummaryJobQueue queue,
    IServiceProvider serviceProvider,
    ILogger<SummaryProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                await ProcessAsync(request, scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Summary job {Kind} for {ConversationId} failed.", request.Kind, request.ConversationId);
            }
            finally
            {
                queue.MarkDone(request);
            }
        }
    }

    /// <summary>Runs one job and publishes its notification.</summary>
    internal static async Task ProcessAsync(SummaryJobRequest request, IServiceProvider services, CancellationToken ct)
    {
        var summariser = services.GetRequiredService<SummarisationService>();
        var notifications = services.GetRequiredService<NotificationPublisher>();
        var logger = services.GetRequiredService<ILogger<SummaryProcessingService>>();

        switch (request.Kind)
        {
            case SummaryJobKind.Record:
                await ProcessRecordAsync(request, summariser, notifications, logger, ct);
                break;

            case SummaryJobKind.Digest:
                await ProcessDigestAsync(request, summariser, services.GetRequiredService<IConversationRepository>(), notifications, ct);
                break;

            case SummaryJobKind.Bulk:
                var result = await summariser.SummariseAsync(ct);
                await notifications.PublishAsync(request.UserId,
                    result.Errors > 0 ? NotificationKind.SummaryFailed : NotificationKind.SummaryReady,
                    "Summaries generated",
                    $"{result.Summarised} summarised, {result.Skipped} skipped (no content), {result.Errors} failed.",
                    ct: ct);
                break;
        }
    }

    private static async Task ProcessRecordAsync(
        SummaryJobRequest request, SummarisationService summariser, NotificationPublisher notifications,
        ILogger logger, CancellationToken ct)
    {
        var id = request.ConversationId!;
        try
        {
            var conversation = await summariser.GenerateRecordAsync(id, ct);
            await notifications.PublishAsync(request.UserId, NotificationKind.RecordReady,
                "Record ready",
                $"The record for “{conversation.Title ?? "Untitled"}” is ready to download.",
                NotificationPublisher.ConversationLink(id), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Record generation failed for conversation {Id}.", id);
            await notifications.PublishAsync(request.UserId, NotificationKind.RecordFailed,
                "Record failed",
                $"The record could not be generated: {ex.Message}",
                NotificationPublisher.ConversationLink(id), ct);
        }
    }

    private static async Task ProcessDigestAsync(
        SummaryJobRequest request, SummarisationService summariser, IConversationRepository repository,
        NotificationPublisher notifications, CancellationToken ct)
    {
        var id = request.ConversationId!;
        var conversation = await repository.GetByIdAsync(id, ct);
        var outcome = conversation is null
            ? DigestOutcome.Failed
            : await summariser.SummariseConversationAsync(conversation, embedAfter: true, ct);

        var title = conversation?.Title ?? "Untitled";
        var (kind, heading, message) = outcome switch
        {
            DigestOutcome.Generated => (NotificationKind.SummaryReady, "Summary ready", $"“{title}” has been summarised and re-embedded."),
            DigestOutcome.Skipped => (NotificationKind.SummaryFailed, "Nothing to summarise", $"“{title}” has no visible content to summarise."),
            _ => (NotificationKind.SummaryFailed, "Summary failed", $"“{title}” could not be summarised."),
        };

        await notifications.PublishAsync(request.UserId, kind, heading, message, NotificationPublisher.ConversationLink(id), ct);
    }
}
