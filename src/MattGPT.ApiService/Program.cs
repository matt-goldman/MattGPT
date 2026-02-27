using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using MattGPT.ApiService;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using OpenAI;
using System.ClientModel;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for large file uploads (up to 250 MB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 262_144_000; // 250 MB
});

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add MongoDB client via Aspire integration.
builder.AddMongoDBClient("mattgptdb");

// Add Qdrant client via Aspire integration.
builder.AddQdrantClient("qdrant");

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ConversationParser>();
builder.Services.AddSingleton<ImportJobStore>();
builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
builder.Services.AddSingleton(Channel.CreateBounded<ImportJobRequest>(new BoundedChannelOptions(50)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
}));
builder.Services.AddHostedService<ImportProcessingService>();
builder.Services.AddScoped<SummarisationService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddSingleton<IQdrantService, QdrantService>();
builder.Services.AddScoped<RagService>();
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

// Allow large multipart form uploads on this service.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 262_144_000; // 250 MB
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register LLM services based on configuration.
var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;

switch (llmOptions.Provider.ToLowerInvariant())
{
    case "ollama":
        // Ollama models running on CPU can take a long time to load and generate,
        // especially with large RAG prompts. Override the default 100s HttpClient
        // timeout for the Ollama-backed HttpClients.
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
            });
        });

        // When launched via the AppHost, connection names are injected as environment
        // variables. When running standalone (e.g. dotnet run), fall back to creating
        // an OllamaApiClient directly from the configured endpoint.
        if (llmOptions.ChatConnectionName is { } chatConnectionName)
        {
            builder.AddOllamaApiClient(chatConnectionName).AddChatClient();
        }
        else
        {
            var chatEndpoint = new Uri(llmOptions.Endpoint);
            builder.Services.AddChatClient(new OllamaSharp.OllamaApiClient(chatEndpoint, llmOptions.ModelId));
        }

        if (llmOptions.EmbeddingConnectionName is { } embeddingConnectionName)
        {
            builder.AddOllamaApiClient(embeddingConnectionName).AddEmbeddingGenerator();
        }
        else
        {
            var embeddingEndpoint = new Uri(llmOptions.Endpoint);
            builder.Services.AddEmbeddingGenerator(
                new OllamaSharp.OllamaApiClient(embeddingEndpoint, embeddingModelId));
        }
        break;

    case "foundrylocal":
        // FoundryLocal uses an OpenAI-compatible API. Local servers do not validate
        // the API key, but the SDK requires a non-null value. Use a placeholder if
        // none is configured; production deployments should set LLM:ApiKey explicitly.
        var foundryClient = new OpenAIClient(
            new ApiKeyCredential(llmOptions.ApiKey ?? "local"),
            new OpenAIClientOptions { Endpoint = new Uri(llmOptions.Endpoint) });
        builder.Services.AddChatClient(foundryClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
        builder.Services.AddEmbeddingGenerator(foundryClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        break;

    case "azureopenai":
        var azureClient = new AzureOpenAIClient(
            new Uri(llmOptions.Endpoint),
            new ApiKeyCredential(llmOptions.ApiKey ?? throw new InvalidOperationException("LLM:ApiKey is required for AzureOpenAI provider.")));
        builder.Services.AddChatClient(azureClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
        builder.Services.AddEmbeddingGenerator(azureClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        break;

    default:
        throw new InvalidOperationException($"Unsupported LLM provider: '{llmOptions.Provider}'. Supported values: Ollama, FoundryLocal, AzureOpenAI.");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Conversation file upload endpoint — enqueues file for background processing.
app.MapPost("/conversations/upload", async (HttpRequest request, ImportJobStore jobStore, Channel<ImportJobRequest> channel) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null)
        return Results.BadRequest("No file provided.");

    if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only .json files are accepted.");

    // Save to a temp file so the HTTP request can complete independently of processing.
    var tempPath = Path.GetTempFileName();
    try
    {
        await using var tempStream = File.OpenWrite(tempPath);
        await using var uploadStream = file.OpenReadStream();
        await uploadStream.CopyToAsync(tempStream);
    }
    catch
    {
        File.Delete(tempPath);
        throw;
    }

    var job = jobStore.CreateJob();
    job.FileName = file.FileName;
    await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath));

    return Results.Accepted($"/conversations/status/{job.JobId}", new
    {
        jobId = job.JobId,
        message = "File received. Processing has been queued.",
        fileName = file.FileName,
        sizeBytes = file.Length,
    });
})
.WithName("UploadConversations")
.DisableAntiforgery();

// Polling endpoint for import job progress.
app.MapGet("/conversations/status/{jobId}", (string jobId, ImportJobStore jobStore) =>
{
    var job = jobStore.GetJob(jobId);
    if (job is null)
        return Results.NotFound(new { message = $"Job '{jobId}' not found." });

    return Results.Ok(new
    {
        jobId = job.JobId,
        fileName = job.FileName,
        status = job.Status.ToString(),
        processedConversations = job.ProcessedConversations,
        errorCount = job.ErrorCount,
        errorMessage = job.ErrorMessage,
        createdAt = job.CreatedAt,
        completedAt = job.CompletedAt,
        embeddingStatus = job.EmbeddingStatus.ToString(),
        embeddedConversations = job.EmbeddedConversations,
        embeddingErrors = job.EmbeddingErrors,
        embeddingSkipped = job.EmbeddingSkipped,
        embeddingErrorMessage = job.EmbeddingErrorMessage,
    });
})
.WithName("GetConversationImportStatus");

// Paginated list of stored conversations.
app.MapGet("/conversations", async (IConversationRepository repository, int page = 1, int pageSize = 20) =>
{
    if (page < 1) page = 1;
    if (pageSize is < 1 or > 100) pageSize = 20;

    var (items, total) = await repository.GetPageAsync(page, pageSize);
    return Results.Ok(new
    {
        page,
        pageSize,
        total,
        items = items.Select(c => new
        {
            conversationId = c.ConversationId,
            title = c.Title,
            createTime = c.CreateTime,
            updateTime = c.UpdateTime,
            defaultModelSlug = c.DefaultModelSlug,
            messageCount = c.LinearisedMessages.Count,
            importTimestamp = c.ImportTimestamp,
            processingStatus = c.ProcessingStatus.ToString(),
        }),
    });
})
.WithName("GetConversations");

// Trigger LLM summarisation for all imported conversations.
app.MapPost("/conversations/summarise", async (SummarisationService summariser, CancellationToken ct) =>
{
    var result = await summariser.SummariseAsync(ct);
    return Results.Ok(new
    {
        summarised = result.Summarised,
        errors = result.Errors,
        skipped = result.Skipped,
    });
})
.WithName("SummariseConversations");

// Trigger embedding generation for all summarised conversations.
app.MapPost("/conversations/embed", async (EmbeddingService embedder, CancellationToken ct) =>
{
    var result = await embedder.EmbedAsync(ct);
    return Results.Ok(new
    {
        embedded = result.Embedded,
        errors = result.Errors,
        skipped = result.Skipped,
    });
})
.WithName("EmbedConversations");

// Search conversations using semantic similarity.
app.MapGet("/search", async (
    string q,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IQdrantService qdrantService,
    CancellationToken ct,
    int limit = 5) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest("Query parameter 'q' is required.");

    if (limit is < 1 or > 100) limit = 5;

    var embeddings = await embeddingGenerator.GenerateAsync([q], cancellationToken: ct);
    var queryVector = embeddings[0].Vector.ToArray();

    var results = await qdrantService.SearchAsync(queryVector, limit, ct);

    return Results.Ok(results.Select(r => new
    {
        conversationId = r.ConversationId,
        score = r.Score,
        title = r.Title,
        summary = r.Summary,
    }));
})
.WithName("SearchConversations");

app.MapGet("/llm/status", async (IChatClient chatClient, IOptions<LlmOptions> options) =>
{
    var opts = options.Value;
    bool reachable;
    string? error = null;

    try
    {
        // Send a minimal prompt with a short timeout to test reachability.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await chatClient.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, cts.Token);
        reachable = response is not null;
    }
    catch (Exception ex)
    {
        reachable = false;
        error = ex.Message;
    }

    return Results.Ok(new
    {
        provider = opts.Provider,
        modelId = opts.ModelId,
        endpoint = opts.Endpoint,
        reachable,
        error
    });
})
.WithName("GetLlmStatus");

// RAG pipeline health / diagnostics endpoint.
app.MapGet("/rag/diagnostics", async (
    IConversationRepository repository,
    IQdrantService qdrantService,
    IOptions<RagOptions> ragOptions,
    IOptions<LlmOptions> llmOptions,
    CancellationToken ct) =>
{
    var opts = ragOptions.Value;
    var llm = llmOptions.Value;

    // 1. MongoDB conversation counts by processing status.
    var statusCounts = await repository.GetStatusCountsAsync(ct);

    // 2. Qdrant point count.
    ulong? qdrantPoints = null;
    string? qdrantError = null;
    try
    {
        qdrantPoints = await qdrantService.GetPointCountAsync(ct);
    }
    catch (Exception ex)
    {
        qdrantError = ex.Message;
    }

    // 3. Derive pipeline status diagnostics.
    var totalConversations = statusCounts.Values.Sum();
    var embedded = statusCounts.GetValueOrDefault(MattGPT.ApiService.Models.ConversationProcessingStatus.Embedded);
    var summarised = statusCounts.GetValueOrDefault(MattGPT.ApiService.Models.ConversationProcessingStatus.Summarised);
    var imported = statusCounts.GetValueOrDefault(MattGPT.ApiService.Models.ConversationProcessingStatus.Imported);
    var summaryErrors = statusCounts.GetValueOrDefault(MattGPT.ApiService.Models.ConversationProcessingStatus.SummaryError);
    var embeddingErrors = statusCounts.GetValueOrDefault(MattGPT.ApiService.Models.ConversationProcessingStatus.EmbeddingError);

    var issues = new List<string>();
    if (totalConversations == 0)
        issues.Add("No conversations in MongoDB. Upload a ChatGPT export via POST /conversations/upload.");
    else if (imported > 0 || summarised > 0)
    {
        var unembedded = imported + summarised;
        issues.Add($"{unembedded} conversations are not yet embedded. Run POST /conversations/embed (or re-upload — embedding is automatic on import).");
    }
    if (summaryErrors > 0)
        issues.Add($"{summaryErrors} conversations failed summarisation (non-blocking — embedding uses raw content).");
    if (embeddingErrors > 0)
        issues.Add($"{embeddingErrors} conversations failed embedding generation.");
    if (qdrantPoints is null)
        issues.Add("Qdrant collection does not exist yet. Run POST /conversations/embed to create it.");
    else if (qdrantPoints == 0)
        issues.Add("Qdrant collection exists but is empty.");
    if (embedded > 0 && qdrantPoints is not null && (long)qdrantPoints.Value < embedded)
        issues.Add($"Qdrant has {qdrantPoints} points but MongoDB has {embedded} embedded conversations — mismatch.");

    var healthy = issues.Count == 0;

    return Results.Ok(new
    {
        healthy,
        issues,
        ragConfig = new { topK = opts.TopK, minScore = opts.MinScore },
        llmConfig = new { provider = llm.Provider, modelId = llm.ModelId, embeddingModelId = llm.EmbeddingModelId },
        mongodb = new
        {
            totalConversations,
            byStatus = statusCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        },
        qdrant = new
        {
            pointCount = qdrantPoints,
            collectionExists = qdrantPoints.HasValue,
            error = qdrantError,
        },
    });
})
.WithName("RagDiagnostics");

// RAG chat endpoint — generates a response augmented with relevant past conversations.
app.MapPost("/chat", async (ChatRequest request, RagService ragService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("'message' is required.");

    var result = await ragService.ChatAsync(request.Message, ct);

    return Results.Ok(new
    {
        answer = result.Answer,
        sources = result.Sources.Select(s => new
        {
            conversationId = s.ConversationId,
            title = s.Title,
            summary = s.Summary,
            score = s.Score,
        }),
    });
})
.WithName("Chat");

// Streaming RAG chat endpoint — returns Server-Sent Events with incremental tokens.
app.MapPost("/chat/stream", async (ChatRequest request, RagService ragService, HttpContext httpContext, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        httpContext.Response.StatusCode = 400;
        await httpContext.Response.WriteAsync("'message' is required.", ct);
        return;
    }

    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";

    await foreach (var chunk in ragService.ChatStreamAsync(request.Message, ct))
    {
        if (chunk.Text is not null)
        {
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
                conversationId = s.ConversationId,
                title = s.Title,
                summary = s.Summary,
                score = s.Score,
            }));
            await httpContext.Response.WriteAsync($"event: sources\ndata: {sourcesJson}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }
    }

    // Signal end of stream.
    await httpContext.Response.WriteAsync("event: done\ndata: [DONE]\n\n", ct);
    await httpContext.Response.Body.FlushAsync(ct);
})
.WithName("ChatStream");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record ChatRequest(string Message);
