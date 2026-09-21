namespace MattGPT.ApiClient.Models;

/// <summary>Response from the file upload endpoint.</summary>
public record UploadResponse(string JobId);

/// <summary>Response from the embeddings endpoint identifying the queued background run.</summary>
public record EmbedJobResponse(string JobId);

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
    string? EmbeddingErrorMessage);

/// <summary>Summary of an imported conversation as shown in the sidebar.</summary>
public record ImportedConversationItem(string ConversationId, string? Title, double? CreateTime, double? UpdateTime, int MessageCount);

/// <summary>Paginated list of standalone (non-project) imported conversations.</summary>
public record StandaloneConversationsResponse(int Page, int PageSize, long Total, List<ImportedConversationItem> Items);

/// <summary>A group of imported conversations belonging to the same GPT template/project.</summary>
public record ProjectItem(string TemplateId, int ConversationCount, string? MostRecentTitle, double? LatestUpdateTime, double? EarliestCreateTime, string? UserName = null);

/// <summary>Paginated list of conversations within a project.</summary>
public record ProjectConversationsResponse(string TemplateId, int Page, int PageSize, long Total, List<ImportedConversationItem> Items);
