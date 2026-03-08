namespace MattGPT.Contracts.Models;


/// <summary>
/// A search result from a vector similarity search, containing the matched conversation's ID, similarity score, and optionally its title and summary for display purposes.
/// </summary>
/// <param name="ConversationId">The ID of the matched conversation.</param>
/// <param name="Score">The similarity score of the match.</param>
/// <param name="Title">The title of the matched conversation, if available.</param>
/// <param name="Summary">The summary of the matched conversation, if available.</param>
public record VectorSearchResult(string ConversationId, float Score, string? Title, string? Summary);

