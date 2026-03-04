using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="IChatService"/>
public sealed class ChatService(IHttpClientFactory factory) : IChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatStreamEvent> StreamChatAsync(
        string message,
        Guid? sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        object requestBody = sessionId.HasValue
            ? new { message, sessionId = sessionId.Value }
            : new { message };

        var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        string? currentEvent = null;
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line["event: ".Length..];
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line["data: ".Length..];

                ChatStreamEvent? evt = currentEvent switch
                {
                    "session" => ParseSessionEvent(data),
                    "token" => ParseTokenEvent(data),
                    "tool_start" => ParseToolStartEvent(data),
                    "tool_end" => new ToolEndChatEvent(),
                    "sources" => ParseSourcesEvent(data),
                    "done" => new DoneChatEvent(),
                    _ => null,
                };

                currentEvent = null;

                if (evt is not null)
                    yield return evt;

                continue;
            }

            // Blank lines are SSE frame separators — nothing to do.
        }
    }

    /// <inheritdoc/>
    public async Task<SessionDetail?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<SessionDetail>($"/chat/sessions/{sessionId}", JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatSessionItem>> GetSessionsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<List<ChatSessionItem>>($"/chat/sessions?limit={limit}", JsonOptions, cancellationToken)
            ?? [];
    }

    /// <inheritdoc/>
    public async Task<ImportedConversationDetail?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        return await client.GetFromJsonAsync<ImportedConversationDetail>($"/conversations/{conversationId}", JsonOptions, cancellationToken);
    }

    // ── SSE event parsers ─────────────────────────────────────────────────

    private static SessionChatEvent? ParseSessionEvent(string data)
    {
        var id = JsonSerializer.Deserialize<Guid>(data);
        return id != Guid.Empty ? new SessionChatEvent(id) : null;
    }

    private static TokenChatEvent? ParseTokenEvent(string data)
    {
        var token = JsonSerializer.Deserialize<string>(data);
        return token is not null ? new TokenChatEvent(token) : null;
    }

    private static ToolStartChatEvent ParseToolStartEvent(string data)
    {
        var evt = JsonSerializer.Deserialize<ToolEventPayload>(data, JsonOptions);
        return new ToolStartChatEvent(evt?.Tool);
    }

    private static SourcesChatEvent? ParseSourcesEvent(string data)
    {
        var sources = JsonSerializer.Deserialize<List<ChatSource>>(data, JsonOptions);
        return sources is not null ? new SourcesChatEvent(sources) : null;
    }

    // Internal DTO used only to deserialise the tool_start event payload.
    private record ToolEventPayload(string? Tool);
}
