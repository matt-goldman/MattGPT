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
    public async Task<UploadResponse?> UploadFileAsync(string fileName, Stream fileStream, bool generateSummaries = false, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        content.Add(streamContent, "file", fileName);
        content.Add(new StringContent(generateSummaries ? "true" : "false"), "generateSummaries");

        using var response = await client.PostAsync("/conversations/upload", content, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.WriteLine("Authentication failed uploading file to back end");
            Console.WriteLine($"Request contained auth token: {response.RequestMessage?.Headers.Authorization}");
            // Retry is only possible when the stream can be seeked back to the beginning.
            // Non-seekable streams (e.g., network or pipe streams) cannot be replayed after the
            // initial request body was consumed, so the retry is skipped in that case.
            if (!await authFailureHandler.HandleAsync(cancellationToken) || !fileStream.CanSeek) return default;
            
            fileStream.Seek(0, SeekOrigin.Begin);
            using var retryContent = new MultipartFormDataContent();
            var retryStreamContent = new StreamContent(fileStream);
            retryStreamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            retryContent.Add(retryStreamContent, "file", fileName);
            retryContent.Add(new StringContent(generateSummaries ? "true" : "false"), "generateSummaries");
            using var retryResponse = await client.PostAsync("/conversations/upload", retryContent, cancellationToken);
            retryResponse.EnsureSuccessStatusCode();
            return await retryResponse.Content.ReadFromJsonAsync<UploadResponse>(JsonOptions, cancellationToken);
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
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync($"/conversations/status/{jobId}", cancellationToken);
                if (!retryResponse.IsSuccessStatusCode) return null;
                return await retryResponse.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, cancellationToken);
            }
            return null;
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
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync("/conversations/projects", cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<List<ProjectItem>>(JsonOptions, cancellationToken)
                    ?? [];
            }
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
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync($"/conversations/projects/{templateId}?page={page}&pageSize={pageSize}", cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<ProjectConversationsResponse>(JsonOptions, cancellationToken);
            }
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
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync($"/conversations/standalone?page={page}&pageSize={pageSize}", cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<StandaloneConversationsResponse>(JsonOptions, cancellationToken);
            }
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
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.PatchAsJsonAsync(
                    $"/conversations/projects/{templateId}/name",
                    new { name },
                    JsonOptions,
                    cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
            }
            return;
        }
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<EmbedJobResponse?> RunEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        using var response = await client.PostAsync("/conversations/embed", null, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.PostAsync("/conversations/embed", null, cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<EmbedJobResponse>(JsonOptions, cancellationToken);
            }
            return default;
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<EmbedJobResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReembedJobResponse?> ReembedAllAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        using var response = await client.PostAsync("/conversations/reembed", null, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.PostAsync("/conversations/reembed", null, cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<ReembedJobResponse>(JsonOptions, cancellationToken);
            }
            return default;
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ReembedJobResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JobStatusResponse?> GetLatestEmbedJobAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync("/conversations/embed/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync("/conversations/embed/latest", cancellationToken);
                if (retryResponse.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
                if (!retryResponse.IsSuccessStatusCode) return null;
                return await retryResponse.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, cancellationToken);
            }
            return null;
        }
        // 204 No Content means no embed run has happened yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<JobStatusResponse>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FailedEmbeddingItem>> GetFailedEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var response = await client.GetAsync("/conversations/embeddings/failed", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await authFailureHandler.HandleAsync(cancellationToken))
            {
                using var retryResponse = await client.GetAsync("/conversations/embeddings/failed", cancellationToken);
                retryResponse.EnsureSuccessStatusCode();
                return await retryResponse.Content.ReadFromJsonAsync<List<FailedEmbeddingItem>>(JsonOptions, cancellationToken)
                    ?? [];
            }
            return [];
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<FailedEmbeddingItem>>(JsonOptions, cancellationToken)
            ?? [];
    }

    /// <inheritdoc/>
    public Task<DiagnosticsSummary?> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        => GetJsonWithAuthRetryAsync<DiagnosticsSummary>("/conversations/diagnostics", cancellationToken);

    /// <inheritdoc/>
    public Task<DiagnosticConversationsResponse?> GetDiagnosticConversationsAsync(
        string? status, string? query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(query)) qs.Add($"q={Uri.EscapeDataString(query)}");
        return GetJsonWithAuthRetryAsync<DiagnosticConversationsResponse>(
            $"/conversations/diagnostics/conversations?{string.Join('&', qs)}", cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ConversationRecordResponse?> GetRecordAsync(string conversationId, CancellationToken cancellationToken = default)
        => GetJsonWithAuthRetryAsync<ConversationRecordResponse>(
            $"/conversations/{Uri.EscapeDataString(conversationId)}/record", cancellationToken);

    /// <inheritdoc/>
    public Task<SummariseQueuedResponse?> GenerateSummariesAsync(CancellationToken cancellationToken = default)
        => PostJsonWithAuthRetryAsync<SummariseQueuedResponse>("/conversations/summarise", cancellationToken);

    /// <inheritdoc/>
    public Task<ConversationRecordResponse?> RequestRecordAsync(string conversationId, CancellationToken cancellationToken = default)
        => PostJsonWithAuthRetryAsync<ConversationRecordResponse>(
            $"/conversations/{Uri.EscapeDataString(conversationId)}/record", cancellationToken);

    /// <summary>
    /// POSTs to <paramref name="url"/> with no body and deserializes the JSON response, retrying once
    /// after a 401 via <see cref="IAuthFailureHandler"/>. Returns default on any non-success response.
    /// </summary>
    private async Task<T?> PostJsonWithAuthRetryAsync<T>(string url, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var response = await client.PostAsync(url, null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (!await authFailureHandler.HandleAsync(cancellationToken)) return default;
            using var retry = await client.PostAsync(url, null, cancellationToken);
            if (!retry.IsSuccessStatusCode) return default;
            return await retry.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// GETs <paramref name="url"/> and deserializes the JSON body, transparently retrying once
    /// after a 401 via <see cref="IAuthFailureHandler"/>. Returns default on 204 or any
    /// non-success response so callers can treat "no data" uniformly.
    /// </summary>
    private async Task<T?> GetJsonWithAuthRetryAsync<T>(string url, CancellationToken ct)
    {
        var client = CreateClient();
        using var response = await client.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (!await authFailureHandler.HandleAsync(ct)) return default;
            using var retry = await client.GetAsync(url, ct);
            if (retry.StatusCode == System.Net.HttpStatusCode.NoContent || !retry.IsSuccessStatusCode) return default;
            return await retry.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || !response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }
}
