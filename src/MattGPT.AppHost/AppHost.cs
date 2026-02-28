var builder = DistributedApplication.CreateBuilder(args);

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

// --- Document DB and vector store configuration ---
var documentDbProvider = builder.Configuration["DocumentDb:Provider"] ?? "MongoDB";
var vectorStoreProvider = builder.Configuration["VectorStore:Provider"] ?? "Qdrant";
var vectorStoreEndpoint = builder.Configuration["VectorStore:Endpoint"];
var vectorStoreApiKey = builder.Configuration["VectorStore:ApiKey"];
var vectorStoreIndexName = builder.Configuration["VectorStore:IndexName"];

// --- Infrastructure resources ---

// Postgres is provisioned when used for document DB, vector store, or both.
var isPostgresDocumentDb = documentDbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
var isPostgresVectorStore = vectorStoreProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

IResourceBuilder<IResourceWithConnectionString>? postgresDb = null;
if (isPostgresDocumentDb || isPostgresVectorStore)
{
    postgresDb = builder.AddPostgres("postgres")
        .WithDataVolume()
        .AddDatabase("mattgptdb");
}

IResourceBuilder<IResourceWithConnectionString>? mongodb = null;
if (!isPostgresDocumentDb)
{
    mongodb = builder.AddMongoDB("mongodb")
        .WithDataVolume()
        .AddDatabase("mattgptdb");
}

// --- API service ---
var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("LLM__Provider", provider)
    .WithEnvironment("LLM__ModelId", modelId)
    .WithEnvironment("LLM__EmbeddingModelId", embeddingModelId)
    .WithEnvironment("DocumentDb__Provider", documentDbProvider)
    .WithEnvironment("VectorStore__Provider", vectorStoreProvider);

if (mongodb is not null)
{
    apiService
        .WithReference(mongodb)
        .WaitFor(mongodb);
}

if (postgresDb is not null)
{
    apiService
        .WithReference(postgresDb)
        .WaitFor(postgresDb);
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

// --- Web frontend ---
builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
