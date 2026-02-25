var builder = DistributedApplication.CreateBuilder(args);

var mongodb = builder.AddMongoDB("mongodb")
    .AddDatabase("mattgptdb");

var qdrant = builder.AddQdrant("qdrant");

var llmProvider = builder.Configuration["LLM:Provider"] ?? "Ollama";
var llmModelId = builder.Configuration["LLM:ModelId"] ?? "llama3.2";
var llmEmbeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? llmModelId;

var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(mongodb)
    .WaitFor(mongodb)
    .WithReference(qdrant)
    .WaitFor(qdrant);

if (llmProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
{
    var ollama = builder.AddOllama("ollama");
    var chatModel = ollama.AddModel(llmModelId);

    apiService
        .WithReference(chatModel)
        .WaitFor(chatModel)
        .WithEnvironment("LLM__Provider", "Ollama")
        .WithEnvironment("LLM__ModelId", llmModelId)
        .WithEnvironment("LLM__EmbeddingModelId", llmEmbeddingModelId)
        .WithEnvironment("LLM__ChatConnectionName", chatModel.Resource.Name);

    if (!string.Equals(llmEmbeddingModelId, llmModelId, StringComparison.OrdinalIgnoreCase))
    {
        var embeddingModel = ollama.AddModel(llmEmbeddingModelId);
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
else
{
    // For FoundryLocal and AzureOpenAI, inject endpoint and credentials from AppHost config.
    apiService
        .WithEnvironment("LLM__Provider", llmProvider)
        .WithEnvironment("LLM__ModelId", llmModelId)
        .WithEnvironment("LLM__EmbeddingModelId", llmEmbeddingModelId)
        .WithEnvironment("LLM__Endpoint", builder.Configuration["LLM:Endpoint"] ?? string.Empty);

    if (builder.Configuration["LLM:ApiKey"] is string apiKey && !string.IsNullOrEmpty(apiKey))
        apiService.WithEnvironment("LLM__ApiKey", apiKey);
}

builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
