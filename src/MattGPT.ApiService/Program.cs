using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using MattGPT.ApiService;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add MongoDB client via Aspire integration.
builder.AddMongoDBClient("mattgptdb");

// Add Qdrant client via Aspire integration.
builder.AddQdrantClient("qdrant");

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<MattGPT.ApiService.Services.ConversationParser>();

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

// Stub endpoint for conversation file upload (full processing wired in issue 006).
app.MapPost("/conversations/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null)
        return Results.BadRequest("No file provided.");

    if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only .json files are accepted.");

    // Drain the stream to simulate receiving the full file.
    await using var stream = file.OpenReadStream();
    await stream.CopyToAsync(Stream.Null);

    return Results.Accepted("/conversations/upload", new { message = "File received. Processing will begin shortly.", fileName = file.FileName, sizeBytes = file.Length });
})
.WithName("UploadConversations")
.DisableAntiforgery();

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
