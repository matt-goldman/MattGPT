using System.Threading.Channels;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Identifies a pending import job and the temp file path containing the uploaded data.
/// </summary>
/// <param name="GenerateSummaries">Whether to generate a digest for each imported conversation before embedding (opt-in; ADR-014).</param>
public record ImportJobRequest(string JobId, string TempFilePath, string? UserId = null, bool GenerateSummaries = false);

/// <summary>
/// Background service that dequeues import job requests and processes them using
/// <see cref="ConversationParser"/>. Progress is tracked in <see cref="ImportJobStore"/>.
/// After a successful import it optionally generates a digest for each imported conversation, then
/// automatically embeds, so conversations are available for RAG queries straight away. Digests come
/// first so they are part of the embedding. When the job ends, the user is notified.
/// </summary>
public class ImportProcessingService(
    Channel<ImportJobRequest> channel,
    ImportJobStore jobStore,
    ConversationParser parser,
    IConversationRepository repository,
    IUserProfileRepository userProfileRepository,
    IServiceProvider serviceProvider,
    ILogger<ImportProcessingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            var job = jobStore.GetJob(request.JobId);
            if (job is null)
            {
                logger.LogWarning("Import job {JobId} not found in store; skipping.", request.JobId);
                TryDeleteTempFile(request.TempFilePath);
                continue;
            }

            job.Status = ImportJobStatus.Processing;
            logger.LogInformation("Starting import job {JobId} from {TempFile}.", request.JobId, request.TempFilePath);

            try
            {
                await using var stream = File.OpenRead(request.TempFilePath);

                var importedIds = new List<string>();
                double? latestProfileCreateTime = null;
                string? latestProfileText = null;
                string? latestProfileInstructions = null;

                await foreach (var conversation in parser.ParseAsync(stream, stoppingToken))
                {
                    try
                    {
                        // Scan for user_editable_context messages to extract the latest user profile.
                        foreach (var message in conversation.Messages)
                        {
                            if (message.Content.ContentType == "user_editable_context" &&
                                message.CreateTime.HasValue &&
                                (!latestProfileCreateTime.HasValue || message.CreateTime > latestProfileCreateTime))
                            {
                                latestProfileCreateTime = message.CreateTime;
                                latestProfileText = message.Content.UserProfile;
                                latestProfileInstructions = message.Content.UserInstructions;
                            }
                        }

                        // Upsert the conversation into MongoDB, then count it.
                        await repository.UpsertAsync(StoredConversation.From(conversation, request.UserId), stoppingToken);
                        importedIds.Add(conversation.Id);
                        job.ProcessedConversations++;
                    }
                    catch (Exception ex)
                    {
                        // Individual conversation processing errors are non-fatal.
                        job.ErrorCount++;
                        logger.LogWarning(ex, "Error processing conversation in job {JobId}; continuing.", request.JobId);
                    }
                }

                // Store latest user profile if found across the import.
                if (latestProfileCreateTime.HasValue)
                {
                    await TryUpdateUserProfileAsync(latestProfileCreateTime.Value, latestProfileText, latestProfileInstructions, stoppingToken);
                }

                job.Status = ImportJobStatus.Complete;
                logger.LogInformation(
                    "Import job {JobId} complete: {Count} conversations processed, {Errors} errors.",
                    request.JobId, job.ProcessedConversations, job.ErrorCount);

                // Digests before embedding, so the embedding includes them.
                if (request.GenerateSummaries)
                    await TrySummariseImportedAsync(job, importedIds, stoppingToken);

                // Auto-trigger embedding generation for newly imported conversations.
                await TryAutoEmbedAsync(request.JobId, stoppingToken);

                await NotifyAsync(request.UserId, NotificationKind.ImportCompleted, "Import complete",
                    DescribeCompletedImport(job));
            }
            catch (OperationCanceledException)
            {
                job.Status = ImportJobStatus.Failed;
                job.ErrorMessage = "Processing was cancelled.";
                logger.LogWarning("Import job {JobId} was cancelled.", request.JobId);
            }
            catch (Exception ex)
            {
                job.Status = ImportJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                logger.LogError(ex, "Import job {JobId} failed.", request.JobId);

                await NotifyAsync(request.UserId, NotificationKind.ImportFailed, "Import failed",
                    $"{job.FileName ?? "The import"} could not be imported: {ex.Message}");
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
                TryDeleteTempFile(request.TempFilePath);
            }
        }
    }

    /// <summary>
    /// Generates a digest for each conversation in this import (opt-in). A failure for one
    /// conversation is counted and logged, never fails the import, and leaves it to be backfilled by
    /// the bulk summarise run. Embedding follows in bulk, so nothing is embedded here.
    /// </summary>
    private async Task TrySummariseImportedAsync(ImportJob job, IReadOnlyList<string> conversationIds, CancellationToken ct)
    {
        job.SummaryStatus = SummaryJobStatus.InProgress;
        try
        {
            using var scope = serviceProvider.CreateScope();
            var summariser = scope.ServiceProvider.GetRequiredService<SummarisationService>();

            logger.LogInformation("Generating digests for {Count} conversations in job {JobId}.", conversationIds.Count, job.JobId);

            foreach (var id in conversationIds)
            {
                ct.ThrowIfCancellationRequested();

                switch (await summariser.SummariseConversationAsync(id, embedAfter: false, ct))
                {
                    case DigestOutcome.Generated: job.SummarisedConversations++; break;
                    case DigestOutcome.Skipped:   job.SummarySkipped++;          break;
                    case DigestOutcome.Failed:    job.SummaryErrors++;           break;
                }
            }

            job.SummaryStatus = SummaryJobStatus.Complete;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // E.g. the summariser couldn't be created. The import itself still succeeded.
            job.SummaryStatus = SummaryJobStatus.Failed;
            logger.LogError(ex, "Digest generation failed for job {JobId}; continuing to embedding.", job.JobId);
        }
    }

    private static string DescribeCompletedImport(ImportJob job)
    {
        var parts = new List<string> { $"{job.FileName ?? "Import"}: {job.ProcessedConversations} conversation(s) imported" };
        if (job.ErrorCount > 0)
            parts.Add($"{job.ErrorCount} could not be parsed");
        if (job.SummaryStatus != SummaryJobStatus.NotRequested)
            parts.Add($"{job.SummarisedConversations} summarised, {job.SummarySkipped} skipped, {job.SummaryErrors} summary failure(s)");
        parts.Add(job.EmbeddingStatus == EmbeddingJobStatus.Failed
            ? "embedding failed"
            : $"{job.EmbeddedConversations} embedded");
        return string.Join("; ", parts) + ".";
    }

    /// <summary>Publishes a notification if notifications are available (they are optional in tests).</summary>
    private async Task NotifyAsync(string? userId, NotificationKind kind, string title, string message)
    {
        var publisher = serviceProvider.GetService<NotificationPublisher>();
        if (publisher is not null)
            await publisher.PublishAsync(userId, kind, title, message, link: kind == NotificationKind.ImportCompleted ? "/chat" : "/upload");
    }

    /// <summary>
    /// Automatically runs the embedding pipeline after a successful import so that
    /// conversations are searchable via RAG without requiring a manual step.
    /// Progress is tracked on the <see cref="ImportJob"/> so the UI can display it.
    /// </summary>
    private async Task TryAutoEmbedAsync(string jobId, CancellationToken ct)
    {
        var job = jobStore.GetJob(jobId);

        try
        {
            logger.LogInformation("Auto-embedding: starting embedding generation for job {JobId}.", jobId);

            if (job is not null)
                job.EmbeddingStatus = EmbeddingJobStatus.InProgress;

            using var scope = serviceProvider.CreateScope();
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
                job.EmbeddingStatus = EmbeddingJobStatus.Complete;
            }

            logger.LogInformation(
                "Auto-embedding complete for job {JobId}: {Embedded} embedded, {Errors} errors, {Skipped} skipped.",
                jobId, result.Embedded, result.Errors, result.Skipped);
        }
        catch (Exception ex)
        {
            if (job is not null)
            {
                job.EmbeddingStatus = EmbeddingJobStatus.Failed;
                job.EmbeddingErrorMessage = ex.Message;
            }

            // Embedding failure should not mark the import as failed — the import succeeded.
            logger.LogError(
                ex,
                "Auto-embedding failed for job {JobId}. Conversations are imported but not yet searchable. " +
                "Run POST /conversations/embed manually to retry.",
                jobId);
        }
    }

    private async Task TryUpdateUserProfileAsync(double createTime, string? profileText, string? instructions, CancellationToken ct)
    {
        try
        {
            var existing = await userProfileRepository.GetAsync(ct);
            if (existing?.SourceCreateTime >= createTime)
            {
                logger.LogDebug(
                    "Skipping user profile update: existing profile (create_time={Existing}) is newer than found profile (create_time={Found}).",
                    existing.SourceCreateTime, createTime);
                return;
            }

            await userProfileRepository.UpsertAsync(new UserProfile
            {
                UserProfileText = profileText,
                UserInstructions = instructions,
                SourceCreateTime = createTime,
                LastUpdated = DateTimeOffset.UtcNow,
            }, ct);

            logger.LogInformation("Updated user profile from import (source create_time={CreateTime}).", createTime);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update user profile during import.");
        }
    }

    private void TryDeleteTempFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete temp file {Path}.", path); }
    }
}
