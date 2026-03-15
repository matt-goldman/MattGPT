using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="IConversationService"/>
public sealed class ConversationService(IHttpClientFactory factory, IAuthFailureHandler authFailureHandler) : IConversationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<UploadResponse?> UploadFileAsync(string fileName, Stream fileStream, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Add(streamContent, "file", fileName);

        using var response = await client.PostAsync("/conversations/upload", content, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UploadResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync($"/conversations/status/{jobId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProjectItem>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync("/conversations/projects", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return [];
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ProjectItem>>(JsonOptions, cancellationToken)
            ?? [];
    }

    /// <inheritdoc/>
    public async Task<ProjectConversationsResponse?> GetProjectConversationsAsync(
        string templateId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync($"/conversations/projects/{templateId}?page={page}&pageSize={pageSize}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectConversationsResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StandaloneConversationsResponse?> GetStandaloneConversationsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync($"/conversations/standalone?page={page}&pageSize={pageSize}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await authFailureHandler.HandleAsync(cancellationToken);
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StandaloneConversationsResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RenameProjectAsync(string templateId, string name, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.PatchAsJsonAsync(
            $"/conversations/projects/{templateId}/name",
            new { name },
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
