using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using System.Threading.Channels;

namespace MattGPT.ApiService.Endpoints;

public static class ConversationsEndpoints
{
    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder app)
    {
        // Conversation file upload endpoint — enqueues file for background processing.
        app.MapPost("/conversations/upload", async (HttpRequest request, ImportJobStore jobStore, Channel<ImportJobRequest> channel) =>
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

            var job = jobStore.CreateJob();
            job.FileName = file.FileName;
            await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath));

            return Results.Accepted($"/conversations/status/{job.JobId}", new
            {
                jobId = job.JobId,
                message = "File received. Processing has been queued.",
                fileName = file.FileName,
                sizeBytes = file.Length,
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

            return Results.Ok(new
            {
                jobId = job.JobId,
                fileName = job.FileName,
                status = job.Status.ToString(),
                processedConversations = job.ProcessedConversations,
                errorCount = job.ErrorCount,
                errorMessage = job.ErrorMessage,
                createdAt = job.CreatedAt,
                completedAt = job.CompletedAt,
                embeddingStatus = job.EmbeddingStatus.ToString(),
                embeddedConversations = job.EmbeddedConversations,
                embeddingErrors = job.EmbeddingErrors,
                embeddingSkipped = job.EmbeddingSkipped,
                embeddingErrorMessage = job.EmbeddingErrorMessage,
            });
        })
        .WithName("GetConversationImportStatus");

        // Paginated list of stored conversations, ordered by most recently updated first.
        app.MapGet("/conversations", async (IConversationRepository repository, int page = 1, int pageSize = 20) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 100) pageSize = 20;

            var (items, total) = await repository.GetPageAsync(page, pageSize);
            return Results.Ok(new
            {
                page,
                pageSize,
                total,
                items = items.Select(c => new
                {
                    conversationId = c.ConversationId,
                    title = c.Title,
                    createTime = c.CreateTime,
                    updateTime = c.UpdateTime,
                    defaultModelSlug = c.DefaultModelSlug,
                    messageCount = c.LinearisedMessages.Count,
                    importTimestamp = c.ImportTimestamp,
                    processingStatus = c.ProcessingStatus.ToString(),
                }),
            });
        })
        .WithName("GetConversations");

        // Trigger LLM summarisation for all imported conversations.
        app.MapPost("/conversations/summarise", async (SummarisationService summariser, CancellationToken ct) =>
        {
            var result = await summariser.SummariseAsync(ct);
            return Results.Ok(new
            {
                summarised = result.Summarised,
                errors = result.Errors,
                skipped = result.Skipped,
            });
        })
        .WithName("SummariseConversations");

        // Trigger embedding generation for all summarised conversations.
        app.MapPost("/conversations/embed", async (EmbeddingService embedder, CancellationToken ct) =>
        {
            var result = await embedder.EmbedAsync(ct);
            return Results.Ok(new
            {
                embedded = result.Embedded,
                errors = result.Errors,
                skipped = result.Skipped,
            });
        })
        .WithName("EmbedConversations");

        // Get a single imported conversation with full message history.
        app.MapGet("/conversations/{conversationId}", async (string conversationId, IConversationRepository repository) =>
        {
            var conversation = await repository.GetByIdAsync(conversationId);
            if (conversation is null)
                return Results.NotFound(new { message = $"Conversation '{conversationId}' not found." });

            return Results.Ok(new
            {
                conversationId = conversation.ConversationId,
                title = conversation.Title,
                createTime = conversation.CreateTime,
                updateTime = conversation.UpdateTime,
                defaultModelSlug = conversation.DefaultModelSlug,
                processingStatus = conversation.ProcessingStatus.ToString(),
                messages = conversation.LinearisedMessages.Select(m => new
                {
                    role = m.Role,
                    content = string.Join("\n", m.Parts),
                    createTime = m.CreateTime,
                }),
            });
        })
        .WithName("GetConversation");

        return app;
    }
}
