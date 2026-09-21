using System.Threading.Channels;
using MattGPT.Contracts.Models;

namespace MattGPT.ApiService.Services;

/// <summary>Identifies a queued standalone embedding run.</summary>
public record EmbedJobRequest(string JobId);

/// <summary>
/// Background service that dequeues standalone embedding runs (triggered via
/// <c>POST /conversations/embed</c>) and executes <see cref="EmbeddingService.EmbedAsync"/>,
/// reporting progress onto the tracked <see cref="ImportJob"/> so the UI can poll it via
/// <c>/conversations/status/{jobId}</c>. This keeps the HTTP request short and lets the user
/// navigate away and return to observe progress.
/// </summary>
public class EmbedProcessingService(
    Channel<EmbedJobRequest> channel,
    ImportJobStore jobStore,
    IServiceProvider serviceProvider,
    ILogger<EmbedProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            var job = jobStore.GetJob(request.JobId);
            if (job is null)
            {
                logger.LogWarning("Embed job {JobId} not found in store; skipping.", request.JobId);
                continue;
            }

            try
            {
                logger.LogInformation("Starting embed job {JobId}.", request.JobId);
                job.EmbeddingStatus = EmbeddingJobStatus.InProgress;

                // EmbeddingService is scoped (it depends on the scoped repository), so resolve it
                // from a fresh scope rather than the singleton background service.
                using var scope = serviceProvider.CreateScope();
                var embedder = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

                // Report progress back to the job so the UI can poll it live.
                var progress = new Progress<EmbeddingProgress>(p =>
                {
                    job.EmbeddedConversations = p.Embedded;
                    job.EmbeddingErrors = p.Errors;
                    job.EmbeddingSkipped = p.Skipped;
                });

                var result = await embedder.EmbedAsync(stoppingToken, progress);

                job.EmbeddedConversations = result.Embedded;
                job.EmbeddingErrors = result.Errors;
                job.EmbeddingSkipped = result.Skipped;
                job.EmbeddingStatus = EmbeddingJobStatus.Complete;
                job.CompletedAt = DateTimeOffset.UtcNow;

                logger.LogInformation(
                    "Embed job {JobId} complete: {Embedded} embedded, {Errors} errors, {Skipped} skipped.",
                    request.JobId, result.Embedded, result.Errors, result.Skipped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.EmbeddingStatus = EmbeddingJobStatus.Failed;
                job.EmbeddingErrorMessage = "Embedding was cancelled.";
                job.CompletedAt = DateTimeOffset.UtcNow;
                logger.LogWarning("Embed job {JobId} was cancelled.", request.JobId);
            }
            catch (Exception ex)
            {
                job.EmbeddingStatus = EmbeddingJobStatus.Failed;
                job.EmbeddingErrorMessage = ex.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                logger.LogError(ex, "Embed job {JobId} failed.", request.JobId);
            }
        }
    }
}
