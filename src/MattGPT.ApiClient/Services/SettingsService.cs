using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService(IHttpClientFactory factory) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<SystemPromptResponse?> GetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<SystemPromptResponse>("/system-prompt", JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveSystemPromptAsync(string? systemPrompt, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.PutAsJsonAsync("/system-prompt", new { systemPrompt }, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<SystemPromptResponse?> ResetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.DeleteAsync("/system-prompt", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SystemPromptResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserProfileResponse?> GetUserProfileAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<UserProfileResponse>("/user-profile", JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveUserProfileAsync(string? userProfileText, string? userInstructions, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.PutAsJsonAsync(
            "/user-profile",
            new { userProfileText, userInstructions },
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
