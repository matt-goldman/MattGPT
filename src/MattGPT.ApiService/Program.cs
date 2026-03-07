using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using MattGPT.ApiService;
using MattGPT.ApiService.Endpoints;
using MattGPT.ApiService.Services;
using OpenAI;
using System.ClientModel;
using System.Security.Claims;
using System.Threading.Channels;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Pinecone;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for large file uploads (up to 250 MB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 262_144_000; // 250 MB
});

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// --- Optional authentication ---
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Read document DB provider early so we can configure Identity's backing store.
builder.Services.Configure<DocumentDbOptions>(builder.Configuration.GetSection(DocumentDbOptions.SectionName));
var documentDbOptions = builder.Configuration.GetSection(DocumentDbOptions.SectionName).Get<DocumentDbOptions>() ?? new DocumentDbOptions();

if (authOptions.Enabled)
{
    // ASP.NET Core Identity with standard API endpoints.
    builder.Services.AddIdentityApiEndpoints<IdentityUser>()
        .AddEntityFrameworkStores<AppIdentityDbContext>();

    // Identity backing store: same Postgres instance when available, otherwise SQLite.
    if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgptdb");
    }
    else
    {
        builder.Services.AddDbContext<AppIdentityDbContext>(options =>
            options.UseSqlite("Data Source=mattgpt-identity.db"));
    }

    // Trusted header scheme for Blazor BFF → API service-to-service calls.
    builder.Services.AddAuthentication()
        .AddScheme<AuthenticationSchemeOptions, ServiceToServiceAuthHandler>(
            ServiceToServiceAuthHandler.SchemeName, null);

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(
                IdentityConstants.BearerScheme,
                IdentityConstants.ApplicationScheme,
                ServiceToServiceAuthHandler.SchemeName)
            .RequireAuthenticatedUser()
            .Build();
    });
}

// --- Document DB configuration (options already read above for Identity backing-store selection) ---

// Track whether the vector store has already been configured (e.g. when Postgres serves both roles).
var vectorStoreConfigured = false;

// Register document DB services based on provider.
if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    // Postgres document DB — register Npgsql data source and Postgres repository implementations.
    builder.AddNpgsqlDataSource("mattgptdb");
    builder.Services.AddSingleton<IConversationRepository, PostgresConversationRepository>();
    builder.Services.AddSingleton<IProjectNameRepository, PostgresProjectNameRepository>();
    builder.Services.AddSingleton<IUserProfileRepository, PostgresUserProfileRepository>();
    builder.Services.AddSingleton<ISystemConfigRepository, PostgresSystemConfigRepository>();
    builder.Services.AddSingleton<IChatSessionRepository, PostgresChatSessionRepository>();

    // If the vector store is also Postgres, configure it here and set the flag.
    var vsOptions = builder.Configuration.GetSection(VectorStoreOptions.SectionName).Get<VectorStoreOptions>() ?? new VectorStoreOptions();
    if (vsOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<IVectorStore, PostgresVectorStore>();
        vectorStoreConfigured = true;
    }
}
else
{
    // MongoDB (default) — register MongoDB client and repository implementations.
    builder.AddMongoDBClient("mattgptdb");
    builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
    builder.Services.AddSingleton<IProjectNameRepository, ProjectNameRepository>();
    builder.Services.AddSingleton<IUserProfileRepository, UserProfileRepository>();
    builder.Services.AddSingleton<ISystemConfigRepository, SystemConfigRepository>();
    builder.Services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
}

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ConversationParser>();
builder.Services.AddSingleton<ImportJobStore>();
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

// Skip vector store registration if it was already configured above (e.g. Postgres serving both roles).
if (!vectorStoreConfigured)
{
    switch (vectorStoreOptions.Provider.ToLowerInvariant())
    {
        case "qdrant":
            builder.AddQdrantClient("qdrant");
            builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
            break;

        case "postgres":
            // Postgres vector store without Postgres document DB — register Npgsql data source if not yet registered.
            if (!documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddNpgsqlDataSource("mattgpt_vectorstore");
            }
            builder.Services.AddSingleton<IVectorStore, PostgresVectorStore>();
            break;

        case "azureaisearch":
            {
                var searchEndpoint = new Uri(vectorStoreOptions.Endpoint
                    ?? throw new InvalidOperationException("VectorStore:Endpoint is required for AzureAISearch provider."));
                var searchCredential = new AzureKeyCredential(vectorStoreOptions.ApiKey
                    ?? throw new InvalidOperationException("VectorStore:ApiKey is required for AzureAISearch provider."));
                var searchClient = new SearchClient(searchEndpoint, vectorStoreOptions.IndexName, searchCredential);
                var indexClient = new SearchIndexClient(searchEndpoint, searchCredential);
                builder.Services.AddSingleton(searchClient);
                builder.Services.AddSingleton(indexClient);
                builder.Services.AddSingleton<IVectorStore, AzureAISearchVectorStore>();
            }
            break;

        case "pinecone":
            {
                var pineconeClient = new PineconeClient(vectorStoreOptions.ApiKey
                    ?? throw new InvalidOperationException("VectorStore:ApiKey is required for Pinecone provider."));
                builder.Services.AddSingleton(pineconeClient);
                builder.Services.AddSingleton<IVectorStore>(sp =>
                    new PineconeVectorStore(
                        pineconeClient,
                        sp.GetRequiredService<ILogger<PineconeVectorStore>>(),
                        vectorStoreOptions.IndexName));
            }
            break;

        case "weaviate":
            {
                var weaviateEndpoint = vectorStoreOptions.Endpoint
                    ?? throw new InvalidOperationException("VectorStore:Endpoint is required for Weaviate provider.");
                builder.Services.AddHttpClient<WeaviateVectorStore>(client =>
                {
                    client.BaseAddress = new Uri(weaviateEndpoint.TrimEnd('/') + "/");
                    if (!string.IsNullOrEmpty(vectorStoreOptions.ApiKey))
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {vectorStoreOptions.ApiKey}");
                });
                builder.Services.AddSingleton<IVectorStore>(sp =>
                    sp.GetRequiredService<WeaviateVectorStore>());
            }
            break;

        default:
            Console.Error.WriteLine($"[WARNING] Unknown VectorStore:Provider '{vectorStoreOptions.Provider}'; falling back to Qdrant.");
            builder.AddQdrantClient("qdrant");
            builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
            break;
    }
}
builder.Services.AddScoped<RagService>();
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();

// Register the search_memories tool when RAG mode supports tool calling (Auto or ToolsOnly).
if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
{
    builder.Services.AddScoped<SearchMemoriesTool>();
}

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

    case "openai":
        // Direct OpenAI API (not via Azure). Uses the same OpenAI SDK but with the
        // official endpoint and a real API key.
        var openaiClient = new OpenAIClient(
            new ApiKeyCredential(llmOptions.ApiKey ?? throw new InvalidOperationException("LLM:ApiKey is required for OpenAI provider.")));
        {
            var chatBuilder = builder.Services.AddChatClient(openaiClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
        builder.Services.AddEmbeddingGenerator(openaiClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        break;

    case "anthropic":
        // Anthropic Claude via the Anthropic.SDK package.
        // Note: Anthropic does not provide an embedding API. Embeddings must be configured
        // separately via LLM:EmbeddingProvider (e.g. "OpenAI") or the app will fail when
        // generating embeddings.
        var anthropicClient = new Anthropic.SDK.AnthropicClient(llmOptions.ApiKey
            ?? throw new InvalidOperationException("LLM:ApiKey is required for Anthropic provider."));
        {
            var chatBuilder = builder.Services.AddChatClient(anthropicClient.Messages);
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
        // Anthropic has no embedding API — embeddings are handled below by the EmbeddingProvider fallback.
        break;

    case "gemini":
        // Google Gemini via GeminiDotnet.Extensions.AI.
        var geminiOptions = new GeminiDotnet.GeminiClientOptions
        {
            ApiKey = llmOptions.ApiKey
                ?? throw new InvalidOperationException("LLM:ApiKey is required for Gemini provider."),
            ModelId = llmOptions.ModelId
        };
        {
            var chatBuilder = builder.Services.AddChatClient(
                new GeminiDotnet.Extensions.AI.GeminiChatClient(geminiOptions));
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
                chatBuilder.UseFunctionInvocation();
        }
        // Gemini embedding support through M.E.AI is limited; use EmbeddingProvider fallback if needed.
        break;

    default:
        throw new InvalidOperationException(
            $"Unsupported LLM provider: '{llmOptions.Provider}'. " +
            "Supported values: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.");
}

// --- Embedding provider fallback ---
// For providers that don't support embeddings natively (Anthropic, Gemini), a separate
// embedding provider can be configured via LLM:EmbeddingProvider. This registers the
// IEmbeddingGenerator after the main LLM switch, overriding or filling the gap.
if (llmOptions.EmbeddingProvider is { } embProvider && !string.IsNullOrWhiteSpace(embProvider))
{
    var embApiKey = llmOptions.EmbeddingApiKey ?? llmOptions.ApiKey;
    var embEndpoint = llmOptions.EmbeddingEndpoint ?? llmOptions.Endpoint;

    switch (embProvider.ToLowerInvariant())
    {
        case "openai":
            var embOpenAI = new OpenAIClient(
                new ApiKeyCredential(embApiKey ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for OpenAI embedding provider.")));
            builder.Services.AddEmbeddingGenerator(embOpenAI.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
            break;

        case "azureopenai":
            var embAzure = new AzureOpenAIClient(
                new Uri(embEndpoint),
                new ApiKeyCredential(embApiKey ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for AzureOpenAI embedding provider.")));
            builder.Services.AddEmbeddingGenerator(embAzure.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
            break;

        case "ollama":
            var embOllamaEndpoint = new Uri(embEndpoint);
            builder.Services.AddEmbeddingGenerator(
                new OllamaSharp.OllamaApiClient(embOllamaEndpoint, embeddingModelId));
            break;

        default:
            throw new InvalidOperationException(
                $"Unsupported LLM:EmbeddingProvider: '{embProvider}'. Supported values: OpenAI, AzureOpenAI, Ollama.");
    }
}

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (authOptions.Enabled)
{
    var authGroup = app.MapGroup("/auth");
    authGroup.MapIdentityApi<IdentityUser>().AllowAnonymous();
    authGroup.MapGet("/me", (HttpContext context) =>
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        return Results.Ok(new
        {
            id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            email = user.FindFirstValue(ClaimTypes.Email),
        });
    }).RequireAuthorization();

    // Ensure Identity database schema exists.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            db.Database.Migrate();
        else
            db.Database.EnsureCreated();
    }
}

app.MapWeatherEndpoints();
app.MapConversationsEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();
app.MapDiagnosticsEndpoints();

app.MapDefaultEndpoints();

app.Run();
