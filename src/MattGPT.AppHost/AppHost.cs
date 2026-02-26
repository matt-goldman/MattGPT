var builder = DistributedApplication.CreateBuilder(args);

// --- LLM configuration (from appsettings.json, env vars, or user secrets) ---
// To change the LLM provider or model, edit appsettings.json, set environment variables
// (e.g. LLM__Provider=AzureOpenAI), or use dotnet user-secrets.
var provider = builder.Configuration["LLM:Provider"] ?? "Ollama";
var modelId = builder.Configuration["LLM:ModelId"] ?? "llama3.2";
var embeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? modelId;
var endpoint = builder.Configuration["LLM:Endpoint"] ?? string.Empty;
var apiKey = builder.Configuration["LLM:ApiKey"];

// --- Infrastructure resources ---
var mongodb = builder.AddMongoDB("mongodb")
    .AddDatabase("mattgptdb");

var qdrant = builder.AddQdrant("qdrant");

// --- API service ---
var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(mongodb)
    .WaitFor(mongodb)
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithEnvironment("LLM__Provider", provider)
    .WithEnvironment("LLM__ModelId", modelId)
    .WithEnvironment("LLM__EmbeddingModelId", embeddingModelId);

if (!string.IsNullOrEmpty(endpoint))
    apiService.WithEnvironment("LLM__Endpoint", endpoint);

if (!string.IsNullOrEmpty(apiKey))
    apiService.WithEnvironment("LLM__ApiKey", apiKey);

// --- Ollama (only when configured as the provider) ---
if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
{
    var ollama = builder.AddOllama("ollama").WithGPUSupport();
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
