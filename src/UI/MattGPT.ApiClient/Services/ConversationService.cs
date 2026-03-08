using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="IConversationService"/>
public sealed class ConversationService(IHttpClientFactory factory) : IConversationService
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

        var response = await client.PostAsync("/conversations/upload", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UploadResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"/conversations/status/{jobId}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProjectItem>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<List<ProjectItem>>("/conversations/projects", JsonOptions, cancellationToken)
            ?? [];
    }

    /// <inheritdoc/>
    public async Task<ProjectConversationsResponse?> GetProjectConversationsAsync(
        string templateId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<ProjectConversationsResponse>(
            $"/conversations/projects/{templateId}?page={page}&pageSize={pageSize}", JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StandaloneConversationsResponse?> GetStandaloneConversationsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<StandaloneConversationsResponse>(
            $"/conversations/standalone?page={page}&pageSize={pageSize}", JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RenameProjectAsync(string templateId, string name, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/conversations/projects/{templateId}/name",
            new { name },
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
