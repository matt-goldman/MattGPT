using System.Threading.Channels;
using MattGPT.ApiService.Models;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Identifies a pending import job and the temp file path containing the uploaded data.
/// </summary>
public record ImportJobRequest(string JobId, string TempFilePath);

/// <summary>
/// Background service that dequeues import job requests and processes them using
/// <see cref="ConversationParser"/>. Progress is tracked in <see cref="ImportJobStore"/>.
/// After a successful import, automatically triggers embedding generation so that
/// conversations are immediately available for RAG queries.
/// </summary>
public class ImportProcessingService : BackgroundService
{
    private readonly Channel<ImportJobRequest> _channel;
    private readonly ImportJobStore _jobStore;
    private readonly ConversationParser _parser;
    private readonly IConversationRepository _repository;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImportProcessingService> _logger;

    public ImportProcessingService(
        Channel<ImportJobRequest> channel,
        ImportJobStore jobStore,
        ConversationParser parser,
        IConversationRepository repository,
        IServiceProvider serviceProvider,
        ILogger<ImportProcessingService> logger)
    {
        _channel = channel;
        _jobStore = jobStore;
        _parser = parser;
        _repository = repository;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var job = _jobStore.GetJob(request.JobId);
            if (job is null)
            {
                _logger.LogWarning("Import job {JobId} not found in store; skipping.", request.JobId);
                TryDeleteTempFile(request.TempFilePath);
                continue;
            }

            job.Status = ImportJobStatus.Processing;
            _logger.LogInformation("Starting import job {JobId} from {TempFile}.", request.JobId, request.TempFilePath);

            try
            {
                await using var stream = File.OpenRead(request.TempFilePath);

                await foreach (var conversation in _parser.ParseAsync(stream, stoppingToken))
                {
                    try
                    {
                        // Upsert the conversation into MongoDB, then count it.
                        await _repository.UpsertAsync(StoredConversation.From(conversation), stoppingToken);
                        job.ProcessedConversations++;
                    }
                    catch (Exception ex)
                    {
                        // Individual conversation processing errors are non-fatal.
                        job.ErrorCount++;
                        _logger.LogWarning(ex, "Error processing conversation in job {JobId}; continuing.", request.JobId);
                    }
                }

                job.Status = ImportJobStatus.Complete;
                _logger.LogInformation(
                    "Import job {JobId} complete: {Count} conversations processed, {Errors} errors.",
                    request.JobId, job.ProcessedConversations, job.ErrorCount);

                // Auto-trigger embedding generation for newly imported conversations.
                await TryAutoEmbedAsync(request.JobId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                job.Status = ImportJobStatus.Failed;
                job.ErrorMessage = "Processing was cancelled.";
                _logger.LogWarning("Import job {JobId} was cancelled.", request.JobId);
            }
            catch (Exception ex)
            {
                job.Status = ImportJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Import job {JobId} failed.", request.JobId);
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
                TryDeleteTempFile(request.TempFilePath);
            }
        }
    }

    /// <summary>
    /// Automatically runs the embedding pipeline after a successful import so that
    /// conversations are searchable via RAG without requiring a manual step.
    /// Progress is tracked on the <see cref="ImportJob"/> so the UI can display it.
    /// </summary>
    private async Task TryAutoEmbedAsync(string jobId, CancellationToken ct)
    {
        var job = _jobStore.GetJob(jobId);

        try
        {
            _logger.LogInformation("Auto-embedding: starting embedding generation for job {JobId}.", jobId);

            if (job is not null)
                job.EmbeddingStatus = Models.EmbeddingJobStatus.InProgress;

            using var scope = _serviceProvider.CreateScope();
            var embedder = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

            // Report progress back to the job so the UI can poll it.
            var progress = job is not null
                ? new Progress<EmbeddingProgress>(p =>
                {
                    job.EmbeddedConversations = p.Embedded;
                    job.EmbeddingErrors = p.Errors;
                    job.EmbeddingSkipped = p.Skipped;
                })
                : null;

            var result = await embedder.EmbedAsync(ct, progress);

            if (job is not null)
            {
                job.EmbeddedConversations = result.Embedded;
                job.EmbeddingErrors = result.Errors;
                job.EmbeddingSkipped = result.Skipped;
                job.EmbeddingStatus = Models.EmbeddingJobStatus.Complete;
            }

            _logger.LogInformation(
                "Auto-embedding complete for job {JobId}: {Embedded} embedded, {Errors} errors, {Skipped} skipped.",
                jobId, result.Embedded, result.Errors, result.Skipped);
        }
        catch (Exception ex)
        {
            if (job is not null)
            {
                job.EmbeddingStatus = Models.EmbeddingJobStatus.Failed;
                job.EmbeddingErrorMessage = ex.Message;
            }

            // Embedding failure should not mark the import as failed — the import succeeded.
            _logger.LogError(
                ex,
                "Auto-embedding failed for job {JobId}. Conversations are imported but not yet searchable. " +
                "Run POST /conversations/embed manually to retry.",
                jobId);
        }
    }

    private void TryDeleteTempFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temp file {Path}.", path); }
    }
}
