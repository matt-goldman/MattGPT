using MattGPT.AnthropicModule;
using MattGPT.ApiService;
using MattGPT.ApiService.Endpoints;
using MattGPT.ApiService.Services;
using MattGPT.AzureModule;
using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using MattGPT.GeminiModule;
using MattGPT.MongoDBModule;
using MattGPT.OllamaModule;
using MattGPT.OpenAIModule;
using MattGPT.PineconeModule;
using MattGPT.PostgresModule;
using MattGPT.QdrantModule;
using MattGPT.WeaviateModule;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Security.Claims;
using System.Threading.Channels;

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
    var isKeycloak = authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

    if (isKeycloak)
    {
        // --- Keycloak path: validate JWTs issued by the Keycloak realm ---
        var keycloakBase = builder.Configuration.GetConnectionString("keycloak")
            ?? builder.Configuration["Auth:Keycloak:ServerUrl"]
            ?? "http://localhost:8080";
        var keycloakRealm = builder.Configuration["Auth:Keycloak:Realm"] ?? "mattgpt";
        var keycloakAuthority = $"{keycloakBase.TrimEnd('/')}/realms/{keycloakRealm}";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = keycloakAuthority;
                options.Audience = builder.Configuration["Auth:Keycloak:Audience"] ?? "account";
                // Allow HTTP for local dev (Aspire internal network uses HTTP),
                // but require HTTPS metadata by default in non-development environments
                // or when using an HTTPS authority.
                var requireHttpsMetadata = !builder.Environment.IsDevelopment()
                    || keycloakAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.TokenValidationParameters.NameClaimType = ClaimTypes.NameIdentifier;
            });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
        });
    }
    else
    {
        // --- Legacy Identity path ---

        // ASP.NET Core Identity with standard API endpoints.
        builder.Services.AddIdentityApiEndpoints<IdentityUser>()
            .AddEntityFrameworkStores<AppIdentityDbContext>();

        // Identity backing store: driven by AuthOptions settings.
        if (authOptions.UseDocumentDbForAuth && documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgptdb");
        }
        else if (!authOptions.UseDocumentDbForAuth && authOptions.AuthDbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddNpgsqlDbContext<AppIdentityDbContext>("mattgpt-identity-db");
        }
        else
        {
            if (authOptions.UseDocumentDbForAuth && !documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[WARNING] Document DB provider '{documentDbOptions.Provider}' does not support bundled Identity storage; " +
                    "falling back to SQLite for auth.");
            }
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
}

// --- Document DB configuration (options already read above for Identity backing-store selection) ---

// Track whether the vector store has already been configured (e.g. when Postgres serves both roles).
var vectorStoreConfigured = false;

// Register document DB services based on provider.
if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.AddPostgresDocumentModule();

    // If the vector store is also Postgres, configure it here and set the flag.
    var vsOptions = builder.Configuration.GetSection(VectorStoreOptions.SectionName).Get<VectorStoreOptions>() ?? new VectorStoreOptions();
    if (vsOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        builder.AddPostgresVectorModule("mattgptdb");
        vectorStoreConfigured = true;
    }
}
else
{
    // MongoDB (default) — register MongoDB client and repository implementations.
    builder.AddMongoDBModule();
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
            builder.AddQdrantModule();
            break;

        case "postgres":
            {
                // Connection "mattgptdb" is already registered when Postgres is also the doc DB provider.
                var pgConn = documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
                    ? "mattgptdb" : "mattgpt_vectorstore";
                builder.AddPostgresVectorModule(pgConn);
            }
            break;

        case "azureaisearch":
            builder.AddAzureAISearchModule();
            break;

        case "pinecone":
            builder.AddPineconeModule();
            break;

        case "weaviate":
            builder.AddWeaviateModule();
            break;

        default:
            Console.Error.WriteLine($"[WARNING] Unknown VectorStore:Provider '{vectorStoreOptions.Provider}'; falling back to Qdrant.");
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
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();

switch (llmOptions.Provider.ToLowerInvariant())
{
    case "ollama":
        builder.AddOllamaModule();
        break;

    case "foundrylocal":
        builder.AddFoundryLocalModule();
        break;

    case "azureopenai":
        builder.AddAzureOpenAIModule();
        break;

    case "openai":
        builder.AddOpenAIModule();
        break;

    case "anthropic":
        builder.AddAnthropicModule();
        break;

    case "gemini":
        builder.AddGeminiModule();
        break;

    default:
        throw new InvalidOperationException(
            $"Unsupported LLM provider: '{llmOptions.Provider}'. " +
            "Supported values: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.");
}

// Embedding provider fallback — for providers without native embeddings (e.g. Anthropic, Gemini).
if (!string.IsNullOrWhiteSpace(llmOptions.EmbeddingProvider))
{
    switch (llmOptions.EmbeddingProvider.ToLowerInvariant())
    {
        case "openai":
            builder.AddOpenAIEmbeddingModule();
            break;

        case "azureopenai":
            builder.AddAzureOpenAIEmbeddingModule();
            break;

        case "ollama":
            builder.AddOllamaEmbeddingModule();
            break;

        default:
            throw new InvalidOperationException(
                $"Unsupported LLM:EmbeddingProvider: '{llmOptions.EmbeddingProvider}'. Supported values: OpenAI, AzureOpenAI, Ollama.");
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

if (authOptions.Enabled && !authOptions.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase))
{
    // Legacy Identity endpoints — not needed when using Keycloak.
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
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    if (documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

app.MapConversationsEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();
app.MapDiagnosticsEndpoints();

app.MapDefaultEndpoints();

app.Run();
