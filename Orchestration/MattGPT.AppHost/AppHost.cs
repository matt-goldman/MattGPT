var builder = DistributedApplication.CreateBuilder(args);

// TODO: organise these regions into extension methods in other satatic classes
#region Configuration
// --- LLM configuration (from appsettings.json, env vars, or user secrets) ---
// To change the LLM provider or model, edit appsettings.json, set environment variables
// (e.g. LLM__Provider=AzureOpenAI), or use dotnet user-secrets.
// These values are used here for infrastructure provisioning decisions AND are seeded
// into Azure App Configuration so that services can read them directly from the config store.
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

// --- Azure App Configuration ---
// Centralises application-level settings so services read config from the store rather than
// relying on environment-variable fan-out from AppHost. In run (local dev) mode, the
// Azure App Configuration emulator runs as a Docker container. The emulator is seeded with
// the values above the first time it starts (set-if-not-exists semantics so developer
// customisations survive restarts). In publish mode the resource maps to a real Azure
// App Configuration store provisioned via Bicep/azd.
var appConfig = builder.AddAzureAppConfiguration("appconfig");

// In run mode the emulator is used and a dedicated ConfigSeeder project handles
// populating the store. Services WaitFor the seeder so they won't read config
// until seeding is complete. In publish mode the real Azure resource is used and
// values are managed externally (portal / CLI / CI).
IResourceBuilder<ProjectResource>? configSeeder = null;
if (builder.ExecutionContext.IsRunMode)
{
    appConfig.RunAsEmulator(emulator => emulator.WithDataVolume());

    // Build a JSON dictionary of the seed-eligible keys and their current values.
    // This is passed as a single environment variable so the ConfigSeeder doesn't
    // need to know about individual key names or replicate the AppHost's config
    // variable list.
    var seedValues = new Dictionary<string, string>();
    foreach (var key in new[]
    {
        "Auth:Enabled", "Auth:Provider",
        "LLM:Provider", "LLM:ModelId", "LLM:EmbeddingModelId",
        "LLM:Endpoint", "LLM:ApiKey",
        "LLM:EmbeddingProvider", "LLM:EmbeddingApiKey", "LLM:EmbeddingEndpoint",
        "RAG:Mode",
        "DocumentDb:Provider",
        "VectorStore:Provider", "VectorStore:Endpoint",
        "VectorStore:ApiKey", "VectorStore:IndexName",
    })
    {
        var value = builder.Configuration[key];
        if (!string.IsNullOrEmpty(value))
            seedValues[key] = value;
    }

    var seedJson = System.Text.Json.JsonSerializer.Serialize(seedValues);

    configSeeder = builder.AddProject<Projects.MattGPT_ConfigSeeder>("configseeder")
        .WithHttpHealthCheck("/health")
        .WithReference(appConfig)
        .WaitFor(appConfig)
        .WithEnvironment("Seed__Json", seedJson);
}

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
// Application-level settings (LLM, auth, DB/VS providers, RAG, etc.) are supplied via
// Azure App Configuration. Infrastructure connection strings are still injected by Aspire
// through WithReference(). The two Aspire-generated Ollama connection names must remain as
// environment variables because their values are produced at runtime by the resource graph.
var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(appConfig)
    .WaitFor(appConfig);

if (configSeeder is not null)
    apiService.WaitFor(configSeeder);

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

// All other application-level config (vector store endpoints/keys, LLM endpoint/key, RAG
// mode, embedding provider, etc.) is now read by the API service directly from Azure App
// Configuration. The AppHost seeds those values into the store above in OnResourceReady.

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
// Auth settings are read from Azure App Configuration (seeded by ConfigSeeder in run mode),
// so no environment-variable passthrough is required here.
var webfrontend = builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(appConfig)
    .WaitFor(appConfig)
    .WithReference(apiService)
    .WaitFor(apiService);

if (configSeeder is not null)
    webfrontend.WaitFor(configSeeder);

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

if (string.Equals(authProvider, "Keycloak", StringComparison.OrdinalIgnoreCase) && keycloak is not null)
{
    var kcTunnel = builder.AddDevTunnel("kcTunnel")
        .WithAnonymousAccess()
        .WithReference(keycloak);

    ios.WithReference(keycloak, kcTunnel);
    android.WithReference(keycloak, kcTunnel);
}

#endregion

builder.Build().Run();
