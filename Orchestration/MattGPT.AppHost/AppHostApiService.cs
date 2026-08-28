namespace MattGPT.AppHost;

/// <summary>
/// Sets up the API service project and its dependencies (databases, vector store, LLM, auth).
/// </summary>
internal static class AppHostApiService
{
    internal static IResourceBuilder<ProjectResource> AddApiService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzureAppConfigurationResource> appConfig,
        IResourceBuilder<ProjectResource>? configSeeder,
        InfraResources infra)
    {
        // Application-level settings (LLM, auth, DB/VS providers, RAG, etc.) are supplied via
        // Azure App Configuration. Infrastructure connection strings are still injected by
        // Aspire through WithReference(). The Aspire-generated Ollama connection names remain
        // as environment variables because their values are produced at runtime.
        var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
            .WithHttpHealthCheck("/health")
            .WithReference(appConfig)
            .WaitFor(appConfig);

        if (configSeeder is not null)
            apiService.WaitFor(configSeeder);

        // --- Document database ---
        bool dbConfigured = false;

        if (infra.MongoDb is not null)
        {
            apiService.WithReference(infra.MongoDb).WaitFor(infra.MongoDb);
            dbConfigured = true;
        }

        if (infra.PostgresDb is not null)
        {
            apiService.WithReference(infra.PostgresDb).WaitFor(infra.PostgresDb);
            dbConfigured = true;
        }

        if (!dbConfigured)
            throw new InvalidOperationException("No document database configured. Please check your configuration.");

        // --- Qdrant (only when configured as the vector store provider) ---
        var vectorStoreProvider = builder.Configuration["VectorStore:Provider"] ?? "Qdrant";
        if (vectorStoreProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase))
        {
            var qdrant = builder.AddQdrant("qdrant").WithDataVolume();
            apiService.WithReference(qdrant).WaitFor(qdrant);
        }

        // --- LLM provider container resources ---
        var provider = builder.Configuration["LLM:Provider"] ?? "Ollama";

        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var modelId = builder.Configuration["LLM:ModelId"] ?? "llama3.2";
            var embeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? modelId;

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
        else if (provider.Equals("FoundryLocal", StringComparison.OrdinalIgnoreCase))
        {
            var modelId = builder.Configuration["LLM:ModelId"]
                ?? throw new InvalidOperationException(
                    "LLM:ModelId is required when LLM:Provider is 'FoundryLocal'.");
            var embeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? modelId;

            // Use Aspire's Azure AI Foundry resource in local mode. RunAsFoundryLocal()
            // switches the resource from a provisioned Azure deployment to a local
            // Foundry runtime — no Azure subscription required.
            var foundry = builder.AddAzureAIFoundry("foundry").RunAsFoundryLocal();

            var chatDeployment = foundry.AddDeployment(
                name: "chat",
                modelName: modelId,
                modelVersion: "1",
                format: "Microsoft");

            apiService
                .WithReference(chatDeployment)
                .WaitFor(chatDeployment)
                .WithEnvironment("LLM__ChatConnectionName", chatDeployment.Resource.Name);

            if (!string.Equals(embeddingModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                var embeddingDeployment = foundry.AddDeployment(
                    name: "embedding",
                    modelName: embeddingModelId,
                    modelVersion: "1",
                    format: "Microsoft");

                apiService
                    .WithReference(embeddingDeployment)
                    .WaitFor(embeddingDeployment)
                    .WithEnvironment("LLM__EmbeddingConnectionName", embeddingDeployment.Resource.Name);
            }
            else
            {
                apiService.WithEnvironment("LLM__EmbeddingConnectionName", chatDeployment.Resource.Name);
            }
        }

        // --- Keycloak ---
        if (infra.Keycloak is not null)
        {
            apiService.WithReference(infra.Keycloak).WaitFor(infra.Keycloak);
        }

        return apiService;
    }
}
