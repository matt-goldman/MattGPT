using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;
using MattGPT.ApiService;
using MattGPT.ApiService.Endpoints;
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
builder.Services.AddSingleton<IProjectNameRepository, ProjectNameRepository>();
builder.Services.AddSingleton(Channel.CreateBounded<ImportJobRequest>(new BoundedChannelOptions(50)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,
}));
builder.Services.AddHostedService<ImportProcessingService>();
builder.Services.AddScoped<SummarisationService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.Configure<VectorStoreOptions>(builder.Configuration.GetSection(VectorStoreOptions.SectionName));
var vectorStoreOptions = builder.Configuration.GetSection(VectorStoreOptions.SectionName).Get<VectorStoreOptions>() ?? new VectorStoreOptions();
switch (vectorStoreOptions.Provider.ToLowerInvariant())
{
    case "qdrant":
        builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
        break;
    default:
        Console.Error.WriteLine($"[WARNING] Unknown VectorStore:Provider '{vectorStoreOptions.Provider}'; falling back to Qdrant.");
        builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
        break;
}
builder.Services.AddScoped<RagService>();
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();

// Register the search_memories tool when RAG mode supports tool calling (Auto or ToolsOnly).
if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
{
    builder.Services.AddScoped<SearchMemoriesTool>();
}

builder.Services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
builder.Services.AddScoped<ChatSessionService>();
builder.Services.Configure<ChatSessionOptions>(builder.Configuration.GetSection(ChatSessionOptions.SectionName));

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
            var chatBuilder = builder.AddOllamaApiClient(chatConnectionName).AddChatClient();
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
        else
        {
            var chatEndpoint = new Uri(llmOptions.Endpoint);
            var chatBuilder = builder.Services.AddChatClient(new OllamaSharp.OllamaApiClient(chatEndpoint, llmOptions.ModelId));
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
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
        {
            var chatBuilder = builder.Services.AddChatClient(foundryClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
        builder.Services.AddEmbeddingGenerator(foundryClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        break;

    case "azureopenai":
        var azureClient = new AzureOpenAIClient(
            new Uri(llmOptions.Endpoint),
            new ApiKeyCredential(llmOptions.ApiKey ?? throw new InvalidOperationException("LLM:ApiKey is required for AzureOpenAI provider.")));
        {
            var chatBuilder = builder.Services.AddChatClient(azureClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
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

app.MapWeatherEndpoints();
app.MapConversationsEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();
app.MapDiagnosticsEndpoints();

app.MapDefaultEndpoints();

app.Run();
