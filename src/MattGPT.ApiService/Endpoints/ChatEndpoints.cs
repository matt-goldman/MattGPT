using MattGPT.ApiService.Services;

namespace MattGPT.ApiService.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // RAG chat endpoint — generates a response augmented with relevant past conversations.
        app.MapPost("/chat", async (ChatRequest request, RagService ragService, ChatSessionService sessionService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest("'message' is required.");

            var session = await sessionService.GetOrCreateAsync(request.SessionId, ct);
            await sessionService.AddUserMessageAsync(session, request.Message, ct);

            var result = await ragService.ChatAsync(request.Message, session, ct);

            await sessionService.AddAssistantMessageAsync(session, result.Answer, ct);

            return Results.Ok(new
            {
                sessionId   = session.SessionId,
                answer      = result.Answer,
                sources     = result.Sources.Select(s => new
                {
                    conversationId  = s.ConversationId,
                    title           = s.Title,
                    summary         = s.Summary,
                    score           = s.Score,
                }),
            });
        })
        .WithName("Chat");

        // Streaming RAG chat endpoint — returns Server-Sent Events with incremental tokens.
        app.MapPost("/chat/stream", async (ChatRequest request, RagService ragService, ChatSessionService sessionService, HttpContext httpContext, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsync("'message' is required.", ct);
                return;
            }

            var session = await sessionService.GetOrCreateAsync(request.SessionId, ct);
            await sessionService.AddUserMessageAsync(session, request.Message, ct);

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            // Send the session ID as the first SSE event so the client can track it.
            var sessionIdJson = System.Text.Json.JsonSerializer.Serialize(session.SessionId);
            await httpContext.Response.WriteAsync($"event: session\ndata: {sessionIdJson}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);

            var fullResponse = new System.Text.StringBuilder();

            await foreach (var chunk in ragService.ChatStreamAsync(request.Message, session, ct))
            {
                if (chunk.ToolStart && chunk.ToolName is not null)
                {
                    // Tool invocation started — emit tool_start event.
                    var toolStartJson = System.Text.Json.JsonSerializer.Serialize(new { tool = chunk.ToolName });
                    await httpContext.Response.WriteAsync($"event: tool_start\ndata: {toolStartJson}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
                else if (chunk.ToolEnd && chunk.ToolName is not null)
                {
                    // Tool invocation completed — emit tool_end event.
                    var toolEndJson = System.Text.Json.JsonSerializer.Serialize(new { tool = chunk.ToolName });
                    await httpContext.Response.WriteAsync($"event: tool_end\ndata: {toolEndJson}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
                else if (chunk.Text is not null)
                {
                    fullResponse.Append(chunk.Text);
                    // Text token — send as a "token" event.
                    var escapedText = System.Text.Json.JsonSerializer.Serialize(chunk.Text);
                    await httpContext.Response.WriteAsync($"event: token\ndata: {escapedText}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
                else if (chunk.Sources is not null)
                {
                    // Final frame — send sources as a "sources" event.
                    var sourcesJson = System.Text.Json.JsonSerializer.Serialize(chunk.Sources.Select(s => new
                    {
                        conversationId  = s.ConversationId,
                        title           = s.Title,
                        summary         = s.Summary,
                        score           = s.Score,
                    }));
                    await httpContext.Response.WriteAsync($"event: sources\ndata: {sourcesJson}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }

            // Persist the assistant's full response.
            if (fullResponse.Length > 0)
            {
                await sessionService.AddAssistantMessageAsync(session, fullResponse.ToString(), ct);
            }

            // Signal end of stream.
            await httpContext.Response.WriteAsync("event: done\ndata: [DONE]\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        })
        .WithName("ChatStream");

        // List recent chat sessions for the sidebar.
        app.MapGet("/chat/sessions", async (IChatSessionRepository sessionRepo, ICurrentUserService currentUser, int limit = 50) =>
        {
            if (limit is < 1 or > 200) limit = 50;
            var sessions = await sessionRepo.ListRecentAsync(limit, currentUser.UserId);
            return Results.Ok(sessions.Select(s => new
            {
                sessionId   = s.SessionId,
                title       = s.Title,
                createdAt   = s.CreatedAt,
                updatedAt   = s.UpdatedAt,
                status      = s.Status.ToString(),
            }));
        })
        .WithName("ListChatSessions");

        // Get a single chat session with full message history.
        app.MapGet("/chat/sessions/{sessionId:guid}", async (Guid sessionId, IChatSessionRepository sessionRepo) =>
        {
            var session = await sessionRepo.GetByIdAsync(sessionId);
            if (session is null)
                return Results.NotFound(new { message = $"Session '{sessionId}' not found." });

            return Results.Ok(new
            {
                sessionId       = session.SessionId,
                title           = session.Title,
                createdAt       = session.CreatedAt,
                updatedAt       = session.UpdatedAt,
                status          = session.Status.ToString(),
                rollingSummary  = session.RollingSummary,
                messages        = session.Messages.Select(m => new
                {
                    role        = m.Role,
                    content     = m.Content,
                    timestamp   = m.Timestamp,
                }),
            });
        })
        .WithName("GetChatSession");

        return app;
    }
}

record ChatRequest(string Message, Guid? SessionId = null);
