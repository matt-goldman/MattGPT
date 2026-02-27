namespace MattGPT.ApiService.Models;

public enum ImportJobStatus { Queued, Processing, Complete, Failed }

public enum EmbeddingJobStatus { NotStarted, InProgress, Complete, Failed }

/// <summary>
/// Tracks the state of a background conversation-import job, including the
/// automatic embedding phase that runs after import completes.
/// </summary>
public class ImportJob
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public string? FileName { get; set; }
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;
    public int ProcessedConversations { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    // — Embedding phase tracking —
    public EmbeddingJobStatus EmbeddingStatus { get; set; } = EmbeddingJobStatus.NotStarted;
    public int EmbeddedConversations { get; set; }
    public int EmbeddingErrors { get; set; }
    public int EmbeddingSkipped { get; set; }
    public string? EmbeddingErrorMessage { get; set; }
}
