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
    ollama.AddModel(llmModelId);

    if (!string.Equals(llmEmbeddingModelId, llmModelId, StringComparison.OrdinalIgnoreCase))
        ollama.AddModel(llmEmbeddingModelId);

    apiService
        .WithReference(ollama)
        .WaitFor(ollama)
        .WithEnvironment("LLM__Provider", "Ollama")
        .WithEnvironment("LLM__ModelId", llmModelId)
        .WithEnvironment("LLM__EmbeddingModelId", llmEmbeddingModelId)
        .WithEnvironment("LLM__Endpoint", ollama.Resource.ConnectionStringExpression);
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
