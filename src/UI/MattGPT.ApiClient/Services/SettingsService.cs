using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="ISettingsService"/>
public sealed class SettingsService(IHttpClientFactory factory, IAuthFailureHandler authFailureHandler) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<SystemPromptResponse?> GetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync("/system-prompt", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SystemPromptResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveSystemPromptAsync(string? systemPrompt, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.PutAsJsonAsync("/system-prompt", new { systemPrompt }, JsonOptions, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<SystemPromptResponse?> ResetSystemPromptAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.DeleteAsync("/system-prompt", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SystemPromptResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UserProfileResponse?> GetUserProfileAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync("/user-profile", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveUserProfileAsync(string? userProfileText, string? userInstructions, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.PutAsJsonAsync(
            "/user-profile",
            new { userProfileText, userInstructions },
            JsonOptions,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return;
        }
        response.EnsureSuccessStatusCode();
    }
}
