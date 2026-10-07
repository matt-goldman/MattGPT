namespace MattGPT.ApiClient.Models;

/// <summary>Response from the file upload endpoint.</summary>
public record UploadResponse(string JobId);

/// <summary>Response from queuing a background summarise run; <see cref="AlreadyRunning"/> when one was already queued.</summary>
public record SummariseQueuedResponse(bool Queued, bool AlreadyRunning);

/// <summary>Response from the embeddings endpoint identifying the queued background run.</summary>
public record EmbedJobResponse(string JobId);

/// <summary>The queued full re-embed, and what it reset.</summary>
public record ReembedJobResponse(
    string JobId, long ConversationsReset, long SessionsReset, string? ChunkingStrategy);

/// <summary>A conversation whose embedding failed and will be retried on the next run.</summary>
public record FailedEmbeddingItem(string ConversationId, string? Title);

/// <summary>Status of a background import/embedding job.</summary>
public record JobStatusResponse(
    string JobId,
    string? FileName,
    string Status,
    int ProcessedConversations,
    int ErrorCount,
    string? ErrorMessage,
    string EmbeddingStatus,
    int EmbeddedConversations,
    int EmbeddingErrors,
    int EmbeddingSkipped,
    string? EmbeddingErrorMessage,
    string SummaryStatus = "NotRequested",
    int SummarisedConversations = 0,
    int SummarySkipped = 0,
    int SummaryErrors = 0);

/// <summary>
/// A conversation's record for export. <see cref="Status"/> is "Ready" (with <see cref="FileName"/> and
/// <see cref="Markdown"/>), "Pending" (being generated) or "None".
/// </summary>
public record ConversationRecordResponse(string Status, string? FileName, string? Markdown);

/// <summary>Summary of an imported conversation as shown in the sidebar.</summary>
public record ImportedConversationItem(string ConversationId, string? Title, double? CreateTime, double? UpdateTime, int MessageCount);

/// <summary>Paginated list of standalone (non-project) imported conversations.</summary>
public record StandaloneConversationsResponse(int Page, int PageSize, long Total, List<ImportedConversationItem> Items);

/// <summary>A group of imported conversations belonging to the same GPT template/project.</summary>
public record ProjectItem(string TemplateId, int ConversationCount, string? MostRecentTitle, double? LatestUpdateTime, double? EarliestCreateTime, string? UserName = null);

/// <summary>Paginated list of conversations within a project.</summary>
public record ProjectConversationsResponse(string TemplateId, int Page, int PageSize, long Total, List<ImportedConversationItem> Items);

/// <summary>Pipeline-health summary shown at the top of the diagnostics view.</summary>
public record DiagnosticsSummary(
    long TotalConversations,
    Dictionary<string, long> ByStatus,
    long? VectorPoints,
    string? VectorStoreError,
    string? LlmProvider,
    string? LlmModelId,
    string? EmbeddingModelId,
    List<string> Issues,
    long DigestsAwaitingEmbedding = 0,
    string? ChunkingStrategy = null,
    Dictionary<string, long>? ByChunkingStrategy = null);

/// <summary>A single conversation's pipeline state in the diagnostics drill-down table.</summary>
public record ConversationDiagnostic(
    string ConversationId,
    string? Title,
    string Status,
    bool HasSummary,
    double? UpdateTime,
    DateTimeOffset? ImportTimestamp,
    string? ChunkingStrategy = null,
    int? ChunkCount = null);

/// <summary>A paged list of diagnostic conversation rows.</summary>
public record DiagnosticConversationsResponse(int Page, int PageSize, long Total, List<ConversationDiagnostic> Items);
