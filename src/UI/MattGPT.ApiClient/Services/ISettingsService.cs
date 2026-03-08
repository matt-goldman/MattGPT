using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>API client for reading and updating application settings.</summary>
public interface ISettingsService
{
    /// <summary>Returns the current system prompt configuration.</summary>
    Task<SystemPromptResponse?> GetSystemPromptAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a new system prompt. Pass <see langword="null"/> or empty to clear it.</summary>
    Task SaveSystemPromptAsync(string? systemPrompt, CancellationToken cancellationToken = default);

    /// <summary>Deletes any stored system prompt, restoring the server default. Returns the resulting default prompt.</summary>
    Task<SystemPromptResponse?> ResetSystemPromptAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current user profile / custom instructions.</summary>
    Task<UserProfileResponse?> GetUserProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the user profile and custom response instructions.</summary>
    Task SaveUserProfileAsync(string? userProfileText, string? userInstructions, CancellationToken cancellationToken = default);
}
