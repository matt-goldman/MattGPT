using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>
/// API client for conversation import operations: uploading files,
/// polling job status, and browsing imported conversations and projects.
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// Uploads a conversations JSON file and returns the resulting job ID. With
    /// <paramref name="generateSummaries"/>, a digest is generated for each conversation before embedding.
    /// </summary>
    Task<UploadResponse?> UploadFileAsync(string fileName, Stream fileStream, bool generateSummaries = false, CancellationToken cancellationToken = default);

    /// <summary>Returns the state of a conversation's record (with the Markdown when it is ready), or null if the conversation doesn't exist.</summary>
    Task<ConversationRecordResponse?> GetRecordAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Returns the conversation's record if it exists, otherwise queues its generation and returns "Pending".</summary>
    Task<ConversationRecordResponse?> RequestRecordAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Returns the current status of a background import/embedding job.</summary>
    Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Returns all GPT projects (conversation groups by template).</summary>
    Task<IReadOnlyList<ProjectItem>> GetProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a paginated list of conversations belonging to the given project.</summary>
    Task<ProjectConversationsResponse?> GetProjectConversationsAsync(string templateId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Returns a paginated list of standalone (non-project) imported conversations.</summary>
    Task<StandaloneConversationsResponse?> GetStandaloneConversationsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Sets a user-friendly display name for a project.</summary>
    Task RenameProjectAsync(string templateId, string name, CancellationToken cancellationToken = default);

    /// <summary>Queues digest generation for every conversation without one; the user is notified when it finishes.</summary>
    Task<SummariseQueuedResponse?> GenerateSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a background embedding run and returns its job id for polling.</summary>
    Task<EmbedJobResponse?> RunEmbeddingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the status of the most recent embedding run, or null if none has run.</summary>
    Task<JobStatusResponse?> GetLatestEmbedJobAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns conversations whose embedding failed and will be retried on the next run.</summary>
    Task<IReadOnlyList<FailedEmbeddingItem>> GetFailedEmbeddingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the pipeline-health summary for the diagnostics view.</summary>
    Task<DiagnosticsSummary?> GetDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged list of conversations with their pipeline state for the diagnostics
    /// drill-down, optionally filtered by processing status and a title substring.
    /// </summary>
    Task<DiagnosticConversationsResponse?> GetDiagnosticConversationsAsync(
        string? status, string? query, int page, int pageSize, CancellationToken cancellationToken = default);
}
