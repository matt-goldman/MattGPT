var builder = DistributedApplication.CreateBuilder(args);

// TODO: organise these regions into extension methods in other satatic classes
#region Configuration
// --- LLM configuration (from appsettings.json, env vars, or user secrets) ---
// To change the LLM provider or model, edit appsettings.json, set environment variables
// (e.g. LLM__Provider=AzureOpenAI), or use dotnet user-secrets.
var provider = builder.Configuration["LLM:Provider"] ?? "Ollama";
var modelId = builder.Configuration["LLM:ModelId"] ?? "llama3.2";
var embeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? modelId;
var endpoint = builder.Configuration["LLM:Endpoint"] ?? string.Empty;
var apiKey = builder.Configuration["LLM:ApiKey"];
var ragMode = builder.Configuration["RAG:Mode"];

// --- Embedding provider fallback (for providers without native embedding APIs) ---
var embeddingProvider = builder.Configuration["LLM:EmbeddingProvider"];
var embeddingApiKey = builder.Configuration["LLM:EmbeddingApiKey"];
var embeddingEndpoint = builder.Configuration["LLM:EmbeddingEndpoint"];

// --- Optional authentication ---
var authEnabled = builder.Configuration["Auth:Enabled"] ?? "false";
var authProvider = builder.Configuration["Auth:Provider"] ?? "Keycloak";

// --- Document DB and vector store configuration ---
var documentDbProvider = builder.Configuration["DocumentDb:Provider"] ?? "MongoDB";
var vectorStoreProvider = builder.Configuration["VectorStore:Provider"] ?? "Qdrant";
var vectorStoreEndpoint = builder.Configuration["VectorStore:Endpoint"];
var vectorStoreApiKey = builder.Configuration["VectorStore:ApiKey"];
var vectorStoreIndexName = builder.Configuration["VectorStore:IndexName"];

#endregion

#region Infrastructure provisioning
// --- Infrastructure resources ---

// Postgres is provisioned when used for document DB, vector store, or both.
var isPostgresDocumentDb = documentDbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
var isPostgresVectorStore = vectorStoreProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

IResourceBuilder<IResourceWithConnectionString>? postgresDb = null;
if (isPostgresDocumentDb || isPostgresVectorStore)
{
    // When Postgres serves as the document DB (with or without the vector store), the shared
    // database name is "mattgptdb". When Postgres is the vector store only, a distinct name
    // "mattgpt_vectorstore" avoids conflicting with the MongoDB "mattgptdb" resource.
    var pgDbName = isPostgresDocumentDb ? "mattgptdb" : "mattgpt_vectorstore";

    postgresDb = builder.AddPostgres("postgres")
        .WithDataVolume()
        .AddDatabase(pgDbName);
}

IResourceBuilder<IResourceWithConnectionString>? mongodb = null;
if (!isPostgresDocumentDb)
{
    mongodb = builder.AddMongoDB("mongodb")
        .WithDataVolume()
        .AddDatabase("mattgptdb");
}

// --- Keycloak (when auth is enabled and provider is Keycloak) ---
IResourceBuilder<KeycloakResource>? keycloak = null;
var isAuthEnabled = bool.TryParse(authEnabled, out var authEnabledBool) && authEnabledBool;
var isKeycloakProvider = authProvider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase);

if (isAuthEnabled && isKeycloakProvider)
{
    keycloak = builder.AddKeycloak("keycloak")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithDataVolume()
        .WithRealmImport(Path.Combine(AppContext.BaseDirectory, "keycloak", "mattgpt-realm.json"));
}

#endregion

# region API service and dependencies
// --- API service ---
var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Enabled", authEnabled)
    .WithEnvironment("Auth__Provider", authProvider)
    .WithEnvironment("LLM__Provider", provider)
    .WithEnvironment("LLM__ModelId", modelId)
    .WithEnvironment("LLM__EmbeddingModelId", embeddingModelId)
    .WithEnvironment("DocumentDb__Provider", documentDbProvider)
    .WithEnvironment("VectorStore__Provider", vectorStoreProvider);

bool dbConfigured = false;

if (mongodb is not null)
{
    apiService
        .WithReference(mongodb)
        .WaitFor(mongodb);

    dbConfigured = true;
}

if (postgresDb is not null)
{
    apiService
        .WithReference(postgresDb)
        .WaitFor(postgresDb);

    dbConfigured = true;
}

if (!dbConfigured)
{
    throw new InvalidOperationException("No document database configured. Please check your configuration.");
}

// --- Qdrant (only when configured as the vector store provider) ---
if (vectorStoreProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase))
{
    var qdrant = builder.AddQdrant("qdrant")
        .WithDataVolume();

    apiService
        .WithReference(qdrant)
        .WaitFor(qdrant);
}

// --- Vector store configuration passthrough ---
if (!string.IsNullOrEmpty(vectorStoreEndpoint))
    apiService.WithEnvironment("VectorStore__Endpoint", vectorStoreEndpoint);

if (!string.IsNullOrEmpty(vectorStoreApiKey))
    apiService.WithEnvironment("VectorStore__ApiKey", vectorStoreApiKey);

if (!string.IsNullOrEmpty(vectorStoreIndexName))
    apiService.WithEnvironment("VectorStore__IndexName", vectorStoreIndexName);

// --- LLM configuration passthrough ---
if (!string.IsNullOrEmpty(endpoint))
    apiService.WithEnvironment("LLM__Endpoint", endpoint);

if (!string.IsNullOrEmpty(apiKey))
    apiService.WithEnvironment("LLM__ApiKey", apiKey);

if (!string.IsNullOrEmpty(ragMode))
    apiService.WithEnvironment("RAG__Mode", ragMode);

// --- Embedding provider fallback passthrough ---
if (!string.IsNullOrEmpty(embeddingProvider))
    apiService.WithEnvironment("LLM__EmbeddingProvider", embeddingProvider);

if (!string.IsNullOrEmpty(embeddingApiKey))
    apiService.WithEnvironment("LLM__EmbeddingApiKey", embeddingApiKey);

if (!string.IsNullOrEmpty(embeddingEndpoint))
    apiService.WithEnvironment("LLM__EmbeddingEndpoint", embeddingEndpoint);

// --- Ollama (only when configured as the provider) ---
if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
{
    var ollama = builder.AddOllama("ollama")
        .WithImageTag("latest")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .WithGPUSupport();

    var chatModel = ollama.AddModel(modelId);

    apiService
        .WithReference(chatModel)
        .WaitFor(chatModel)
        .WithEnvironment("LLM__ChatConnectionName", chatModel.Resource.Name);

    if (!string.Equals(embeddingModelId, modelId, StringComparison.OrdinalIgnoreCase))
    {
        var embeddingModel = ollama.AddModel(embeddingModelId);
        apiService
            .WithReference(embeddingModel)
            .WaitFor(embeddingModel)
            .WithEnvironment("LLM__EmbeddingConnectionName", embeddingModel.Resource.Name);
    }
    else
    {
        apiService.WithEnvironment("LLM__EmbeddingConnectionName", chatModel.Resource.Name);
    }
}

#endregion

#region UI

// --- Web frontend ---
var webfrontend = builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Enabled", authEnabled)
    .WithEnvironment("Auth__Provider", authProvider)
    .WithReference(apiService)
    .WaitFor(apiService);

// --- Wire up Keycloak to API service and web frontend ---
if (keycloak is not null)
{
    apiService
        .WithReference(keycloak)
        .WaitFor(keycloak);

    webfrontend
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// --- Dev tunnel for secure external access to the API ---
var tunnel = builder.AddDevTunnel("tunnel")
    .WaitFor(apiService)
    .WithAnonymousAccess()
    .WithReference(apiService.GetEndpoint("https"));

var mauiapp = builder.AddMauiProject("mauiapp", @"../../src/UI/MattGPT.Mobile/MattGPT.Mobile.csproj");

// Add Windows device (uses localhost directly)
mauiapp.AddWindowsDevice()
    .WaitFor(apiService)
    .WithReference(apiService);

// Add Mac Catalyst device (uses localhost directly)
mauiapp.AddMacCatalystDevice()
    .WaitFor(apiService)
    .WithReference(apiService);

// Add iOS simulator with Dev Tunnel
var ios = mauiapp.AddiOSSimulator()
    .WaitFor(apiService)
    .WithOtlpDevTunnel() // Required for OpenTelemetry data collection
    .WithReference(apiService, tunnel);

// Add Android emulator with Dev Tunnel
var android = mauiapp.AddAndroidEmulator()
    .WaitFor(apiService)
    .WithOtlpDevTunnel() // Required for OpenTelemetry data collection
    .WithReference(apiService, tunnel);

if (authProvider == "Keycloak" && keycloak is not null)
{
    var kcTunnel = builder.AddDevTunnel("kcTunnel")
        .WithAnonymousAccess()
        .WithReference(keycloak);

    ios.WithReference(keycloak, kcTunnel);
    android.WithReference(keycloak, kcTunnel);
}

#endregion

builder.Build().Run();
