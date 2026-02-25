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
        var ollamaEndpoint = new Uri(llmOptions.Endpoint);
        builder.Services.AddChatClient(new OllamaChatClient(ollamaEndpoint, llmOptions.ModelId));
        builder.Services.AddEmbeddingGenerator(new OllamaEmbeddingGenerator(ollamaEndpoint, embeddingModelId));
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
        status = job.Status.ToString(),
        processedConversations = job.ProcessedConversations,
        errorCount = job.ErrorCount,
        errorMessage = job.ErrorMessage,
        createdAt = job.CreatedAt,
        completedAt = job.CompletedAt,
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

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
