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
/// </summary>
public class ImportProcessingService : BackgroundService
{
    private readonly Channel<ImportJobRequest> _channel;
    private readonly ImportJobStore _jobStore;
    private readonly ConversationParser _parser;
    private readonly ILogger<ImportProcessingService> _logger;

    public ImportProcessingService(
        Channel<ImportJobRequest> channel,
        ImportJobStore jobStore,
        ConversationParser parser,
        ILogger<ImportProcessingService> logger)
    {
        _channel = channel;
        _jobStore = jobStore;
        _parser = parser;
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
                        // Conversation is already linearised by the parser; count it.
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

    private void TryDeleteTempFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temp file {Path}.", path); }
    }
}
