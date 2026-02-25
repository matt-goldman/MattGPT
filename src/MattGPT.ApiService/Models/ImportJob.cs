namespace MattGPT.ApiService.Models;

public enum ImportJobStatus { Queued, Processing, Complete, Failed }

/// <summary>
/// Tracks the state of a background conversation-import job.
/// </summary>
public class ImportJob
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public ImportJobStatus Status { get; set; } = ImportJobStatus.Queued;
    public int ProcessedConversations { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
