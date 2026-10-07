using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.ApiService.Services;
using System.Threading.Channels;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Endpoints;

public static class ConversationsEndpoints
{
    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder app)
    {
        // Conversation file upload endpoint — enqueues file for background processing.
        app.MapPost("/conversations/upload", async (HttpRequest request, ImportJobStore jobStore, Channel<ImportJobRequest> channel, ICurrentUserService currentUser) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file is null)
                return Results.BadRequest("No file provided.");

            if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Only .json files are accepted.");

            // Save to a temp file so the HTTP request can complete independently of processing.
            var tempPath = Path.GetTempFileName();
            try
            {
                await using var tempStream = File.OpenWrite(tempPath);
                await using var uploadStream = file.OpenReadStream();
                await uploadStream.CopyToAsync(tempStream);
            }
            catch
            {
                File.Delete(tempPath);
                throw;
            }

            // Opt-in: digest generation adds an LLM call per conversation (ADR-014).
            var generateSummaries = bool.TryParse(form["generateSummaries"], out var gs) && gs;

            var job = jobStore.CreateJob();
            job.FileName = file.FileName;
            job.UserId = currentUser.UserId;
            if (generateSummaries)
                job.SummaryStatus = SummaryJobStatus.Pending;
            await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath, currentUser.UserId, generateSummaries));

            return Results.Accepted($"/conversations/status/{job.JobId}", new
            {
                jobId       = job.JobId,
                message     = "File received. Processing has been queued.",
                fileName    = file.FileName,
                sizeBytes   = file.Length,
            });
        })
        .WithName("UploadConversations")
        .DisableAntiforgery();

        // Polling endpoint for import job progress.
        app.MapGet("/conversations/status/{jobId}", (string jobId, ImportJobStore jobStore) =>
        {
            var job = jobStore.GetJob(jobId);
            if (job is null)
                return Results.NotFound(new { message = $"Job '{jobId}' not found." });

            return Results.Ok(ToJobStatusResponse(job));
        })
        .WithName("GetConversationImportStatus");

        // Paginated list of stored conversations, ordered by most recently updated first.
        app.MapGet("/conversations", async (IConversationRepository repository, ICurrentUserService currentUser, int page = 1, int pageSize = 20) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 20;

            var (items, total) = await repository.GetPageAsync(page, pageSize, currentUser.UserId);
            return Results.Ok(new
            {
                page,
                pageSize,
                total,
                items = items.Select(c => new
                {
                    conversationId      = c.ConversationId,
                    title               = c.Title,
                    createTime          = c.CreateTime,
                    updateTime          = c.UpdateTime,
                    defaultModelSlug    = c.DefaultModelSlug,
                    messageCount        = c.LinearisedMessages.Count,
                    importTimestamp     = c.ImportTimestamp,
                    processingStatus    = c.ProcessingStatus.ToString(),
                }),
            });
        })
        .WithName("GetConversations");

        // Queue digest generation for every conversation without a digest. Each digest is embedded as
        // it is generated. Runs in the background (it can take hours on a large library); the user
        // is notified when it finishes.
        app.MapPost("/conversations/summarise", (SummaryJobQueue queue, ICurrentUserService currentUser) =>
        {
            var queued = queue.TryEnqueue(new SummaryJobRequest(SummaryJobKind.Bulk, null, currentUser.UserId));
            return Results.Accepted(value: new { queued, alreadyRunning = !queued });
        })
        .WithName("SummariseConversations");

        // Queue digest generation (and re-embedding) for one conversation.
        app.MapPost("/conversations/{conversationId}/summarise", async (
            string conversationId, IConversationRepository repository, SummaryJobQueue queue, ICurrentUserService currentUser) =>
        {
            if (await repository.GetByIdAsync(conversationId) is null)
                return Results.NotFound(new { message = $"Conversation '{conversationId}' not found." });

            var queued = queue.TryEnqueue(new SummaryJobRequest(SummaryJobKind.Digest, conversationId, currentUser.UserId));
            return Results.Accepted(value: new { queued, alreadyRunning = !queued });
        })
        .WithName("SummariseConversation");

        // The conversation's record for export: Ready (with the Markdown file), Pending (being
        // generated), or None.
        app.MapGet("/conversations/{conversationId}/record", async (
            string conversationId, IConversationRepository repository, SummaryJobQueue queue) =>
        {
            var conversation = await repository.GetByIdAsync(conversationId);
            return conversation is null
                ? Results.NotFound(new { message = $"Conversation '{conversationId}' not found." })
                : Results.Ok(ToRecordResponse(conversation, queue));
        })
        .WithName("GetConversationRecord");

        // Request the conversation's record: returns it if it exists, otherwise queues generation for
        // this conversation only (de-duplicated) and returns Pending. The user is notified when it's ready.
        app.MapPost("/conversations/{conversationId}/record", async (
            string conversationId, IConversationRepository repository, SummaryJobQueue queue, ICurrentUserService currentUser) =>
        {
            var conversation = await repository.GetByIdAsync(conversationId);
            if (conversation is null)
                return Results.NotFound(new { message = $"Conversation '{conversationId}' not found." });

            if (string.IsNullOrWhiteSpace(conversation.Record))
                queue.TryEnqueue(new SummaryJobRequest(SummaryJobKind.Record, conversationId, currentUser.UserId));

            return Results.Ok(ToRecordResponse(conversation, queue));
        })
        .WithName("RequestConversationRecord");

        // Queue a standalone embedding run in the background and return the job id to poll.
        // Embedding a large library can take minutes, so this returns immediately rather than
        // blocking the request; progress is polled via /conversations/status/{jobId}.
        app.MapPost("/conversations/embed", async (ImportJobStore jobStore, Channel<EmbedJobRequest> channel, ICurrentUserService currentUser) =>
        {
            var job = jobStore.CreateEmbedJob();
            job.UserId = currentUser.UserId;
            await channel.Writer.WriteAsync(new EmbedJobRequest(job.JobId, currentUser.UserId));

            return Results.Accepted($"/conversations/status/{job.JobId}", new { jobId = job.JobId });
        })
        .WithName("EmbedConversations");

        // Returns the status of the most recent embedding run, or 204 if none has run this session.
        // Lets the UI resume showing progress after a page reload.
        app.MapGet("/conversations/embed/latest", (ImportJobStore jobStore) =>
        {
            var job = jobStore.GetLatestEmbedJob();
            return job is null
                ? Results.NoContent()
                : Results.Ok(ToJobStatusResponse(job));
        })
        .WithName("GetLatestEmbedJob");

        // Returns conversations whose embedding failed (status EmbeddingError) so the UI can list
        // them. These are exactly the conversations a subsequent embed run will retry.
        app.MapGet("/conversations/embeddings/failed", async (IConversationRepository repository, CancellationToken ct) =>
        {
            const int maxFailed = 200;
            var failed = await repository.GetByStatusesAsync([ConversationProcessingStatus.EmbeddingError], maxFailed, ct: ct);
            return Results.Ok(failed.Select(c => new
            {
                conversationId  = c.ConversationId,
                title           = c.Title,
            }));
        })
        .WithName("GetFailedEmbeddings");

        // Diagnostics summary: pipeline health at a glance (status counts, vector-store point count,
        // config, and derived issues), scoped to the current user.
        app.MapGet("/conversations/diagnostics", async (
            IConversationRepository repository,
            IVectorStore vectorStore,
            IOptions<LlmOptions> llmOptions,
            ICurrentUserService currentUser,
            CancellationToken ct) =>
        {
            var byStatus = await repository.GetStatusCountsAsync(currentUser.UserId, ct);
            var total = byStatus.Values.Sum();
            var digestsAwaitingEmbedding = await repository.CountDigestsAwaitingEmbeddingAsync(currentUser.UserId, ct);

            long? vectorPoints = null;
            string? vectorError = null;
            try { vectorPoints = (long?)await vectorStore.GetPointCountAsync(ct); }
            catch (Exception ex) { vectorError = ex.Message; }

            var imported = byStatus.GetValueOrDefault(ConversationProcessingStatus.Imported);
            var summarised = byStatus.GetValueOrDefault(ConversationProcessingStatus.Summarised);
            var embedded = byStatus.GetValueOrDefault(ConversationProcessingStatus.Embedded);
            var summaryErrors = byStatus.GetValueOrDefault(ConversationProcessingStatus.SummaryError);
            var embeddingErrors = byStatus.GetValueOrDefault(ConversationProcessingStatus.EmbeddingError);

            var issues = new List<string>();
            if (total == 0)
                issues.Add("No conversations imported yet.");
            if (imported + summarised > 0)
                issues.Add($"{imported + summarised} conversation(s) not yet embedded.");
            if (summaryErrors > 0)
                issues.Add($"{summaryErrors} conversation(s) failed summarisation before digest status was tracked; the next embed run embeds them from raw content.");
            if (digestsAwaitingEmbedding > 0)
                issues.Add($"{digestsAwaitingEmbedding} digest(s) are not yet in their conversation's embedding; run embeddings to reconcile.");
            if (embeddingErrors > 0)
                issues.Add($"{embeddingErrors} conversation(s) failed embedding; the next embed run retries them.");
            if (vectorError is not null)
                issues.Add($"Vector store unreachable: {vectorError}");
            else if (vectorPoints is not null && embedded > vectorPoints)
                issues.Add($"{embedded} embedded conversation(s) but only {vectorPoints} vector(s) stored — mismatch.");

            var llm = llmOptions.Value;
            return Results.Ok(new
            {
                totalConversations  = total,
                byStatus            = byStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                vectorPoints,
                digestsAwaitingEmbedding,
                vectorStoreError    = vectorError,
                llmProvider         = llm.Provider,
                llmModelId          = llm.ModelId,
                embeddingModelId    = llm.EmbeddingModelId,
                issues,
            });
        })
        .WithName("GetConversationDiagnostics");

        // Diagnostics drill-down: a paged, optionally status/title-filtered list of conversations
        // with their pipeline state. Built from a field projection (not a full deserialize).
        app.MapGet("/conversations/diagnostics/conversations", async (
            IConversationRepository repository,
            ICurrentUserService currentUser,
            string? status,
            string? q,
            int page,
            int pageSize,
            CancellationToken ct) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 200) pageSize = 25;

            ConversationProcessingStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<ConversationProcessingStatus>(status, ignoreCase: true, out var parsed))
            {
                statusFilter = parsed;
            }

            var (items, total) = await repository.GetDiagnosticsPageAsync(
                statusFilter, page, pageSize, q, currentUser.UserId, ct);

            return Results.Ok(new
            {
                page,
                pageSize,
                total,
                items = items.Select(r => new
                {
                    conversationId  = r.ConversationId,
                    title           = r.Title,
                    status          = r.Status.ToString(),
                    hasSummary      = r.HasSummary,
                    updateTime      = r.UpdateTime,
                    importTimestamp = r.ImportTimestamp,
                }),
            });
        })
        .WithName("GetConversationDiagnosticsList");

        // Get a single imported conversation with its message history.
        // Only dialogue is returned by default: tool traffic (file_search results, browser
        // fetches, python output) and scaffolding are excluded, because they are vastly longer
        // than the dialogue and make the conversation view unreadable.
        // Pass ?includeHidden=true to include every stored message, which is how tool results
        // remain reachable for diagnostics and for the planned file/tool-result views.
        app.MapGet("/conversations/{conversationId}", async (string conversationId, bool? includeHidden, IConversationRepository repository) =>
        {
            var conversation = await repository.GetByIdAsync(conversationId);
            if (conversation is null)
                return Results.NotFound(new { message = $"Conversation '{conversationId}' not found." });

            var messages = conversation.LinearisedMessages.AsEnumerable();
            if (includeHidden != true)
            {
                messages = messages.Where(m => m.IsConversational);
            }

            return Results.Ok(new
            {
                conversationId      = conversation.ConversationId,
                title               = conversation.Title,
                createTime          = conversation.CreateTime,
                updateTime          = conversation.UpdateTime,
                defaultModelSlug    = conversation.DefaultModelSlug,
                processingStatus    = conversation.ProcessingStatus.ToString(),
                messages            = messages.Select(m => new
                {
                    role = m.Role,
                    content = string.Join("\n", m.Parts),
                    createTime = m.CreateTime,
                }),
            });
        })
        .WithName("GetConversation");

        // Get all project groups (conversations grouped by ConversationTemplateId for snorlax gizmo type).
        // Merges user-assigned project names when available.
        app.MapGet("/conversations/projects", async (IConversationRepository repository, IProjectNameRepository projectNames, ICurrentUserService currentUser) =>
        {
            var projectsTask = repository.GetProjectsAsync(currentUser.UserId);
            var namesTask = projectNames.GetAllNamesAsync();
            await Task.WhenAll(projectsTask, namesTask);

            var projects = projectsTask.Result;
            var names = namesTask.Result;

            return Results.Ok(projects.Select(p => new
            {
                templateId          = p.TemplateId,
                conversationCount   = p.ConversationCount,
                mostRecentTitle     = p.MostRecentTitle,
                latestUpdateTime    = p.LatestUpdateTime,
                earliestCreateTime  = p.EarliestCreateTime,
                userName            = names.GetValueOrDefault(p.TemplateId),
            }));
        })
        .WithName("GetProjects");

        // Set or update a user-assigned project display name.
        app.MapPatch("/conversations/projects/{templateId}/name", async (
            string templateId, ProjectNameRequest request, IProjectNameRepository projectNames) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Name is required." });
            await projectNames.SetNameAsync(templateId, request.Name.Trim());
            return Results.Ok(new { templateId, name = request.Name.Trim() });
        })
        .WithName("SetProjectName");

        // Get conversations within a specific project, paginated.
        app.MapGet("/conversations/projects/{templateId}", async (
            string templateId, IConversationRepository repository, ICurrentUserService currentUser, int page = 1, int pageSize = 50) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 50;

            var (items, total) = await repository.GetProjectConversationsAsync(templateId, page, pageSize, currentUser.UserId);
            return Results.Ok(new
            {
                templateId,
                page,
                pageSize,
                total,
                items = items.Select(c => new
                {
                    conversationId  = c.ConversationId,
                    title           = c.Title,
                    createTime      = c.CreateTime,
                    updateTime      = c.UpdateTime,
                    messageCount    = c.LinearisedMessages.Count,
                }),
            });
        })
        .WithName("GetProjectConversations");

        // Get non-project conversations (imported conversations not belonging to any project), paginated.
        app.MapGet("/conversations/standalone", async (
            IConversationRepository repository, ICurrentUserService currentUser, int page = 1, int pageSize = 50) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 50;

            var (items, total) = await repository.GetNonProjectConversationsAsync(page, pageSize, currentUser.UserId);
            return Results.Ok(new
            {
                page,
                pageSize,
                total,
                items = items.Select(c => new
                {
                    conversationId  = c.ConversationId,
                    title           = c.Title,
                    createTime      = c.CreateTime,
                    updateTime      = c.UpdateTime,
                    messageCount    = c.LinearisedMessages.Count,
                }),
            });
        })
        .WithName("GetStandaloneConversations");

        return app;
    }

    /// <summary>Shapes a conversation's record state for the record endpoints.</summary>
    private static object ToRecordResponse(StoredConversation conversation, SummaryJobQueue queue)
    {
        if (!string.IsNullOrWhiteSpace(conversation.Record))
        {
            return new
            {
                status      = "Ready",
                fileName    = ConversationRecordExport.FileName(conversation.Title),
                markdown    = ConversationRecordExport.BuildMarkdown(conversation),
            };
        }

        return new
        {
            status      = queue.IsPending(SummaryJobKind.Record, conversation.ConversationId) ? "Pending" : "None",
            fileName    = (string?)null,
            markdown    = (string?)null,
        };
    }

    /// <summary>Shapes an <see cref="ImportJob"/> for the status/latest-embed polling endpoints.</summary>
    private static object ToJobStatusResponse(ImportJob job) => new
    {
        jobId                   = job.JobId,
        fileName                = job.FileName,
        status                  = job.Status.ToString(),
        processedConversations  = job.ProcessedConversations,
        errorCount              = job.ErrorCount,
        errorMessage            = job.ErrorMessage,
        createdAt               = job.CreatedAt,
        completedAt             = job.CompletedAt,
        embeddingStatus         = job.EmbeddingStatus.ToString(),
        embeddedConversations   = job.EmbeddedConversations,
        embeddingErrors         = job.EmbeddingErrors,
        embeddingSkipped        = job.EmbeddingSkipped,
        embeddingErrorMessage   = job.EmbeddingErrorMessage,
        summaryStatus           = job.SummaryStatus.ToString(),
        summarisedConversations = job.SummarisedConversations,
        summarySkipped          = job.SummarySkipped,
        summaryErrors           = job.SummaryErrors,
    };
}

/// <summary>Request body for setting a project display name.</summary>
public record ProjectNameRequest(string Name);
