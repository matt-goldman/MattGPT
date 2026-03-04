namespace MattGPT.ApiClient.Models;

/// <summary>Current system prompt configuration.</summary>
public record SystemPromptResponse(string? SystemPrompt, bool IsDefault, DateTimeOffset? LastUpdated);

/// <summary>Current user profile / custom instructions.</summary>
public record UserProfileResponse(string? UserProfileText, string? UserInstructions, DateTimeOffset? LastUpdated);
