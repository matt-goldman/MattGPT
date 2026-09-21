using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>
/// API client for conversation import operations: uploading files,
/// polling job status, and browsing imported conversations and projects.
/// </summary>
public interface IConversationService
{
    /// <summary>Uploads a conversations JSON file and returns the resulting job ID.</summary>
    Task<UploadResponse?> UploadFileAsync(string fileName, Stream fileStream, CancellationToken cancellationToken = default);

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

    /// <summary>Queues a background embedding run and returns its job id for polling.</summary>
    Task<EmbedJobResponse?> RunEmbeddingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the status of the most recent embedding run, or null if none has run.</summary>
    Task<JobStatusResponse?> GetLatestEmbedJobAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns conversations whose embedding failed and will be retried on the next run.</summary>
    Task<IReadOnlyList<FailedEmbeddingItem>> GetFailedEmbeddingsAsync(CancellationToken cancellationToken = default);
}
