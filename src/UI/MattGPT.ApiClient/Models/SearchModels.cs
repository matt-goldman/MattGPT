namespace MattGPT.ApiClient.Models;

/// <summary>A single semantic search result.</summary>
public record SearchResult(string ConversationId, float Score, string? Title, string? Summary);
