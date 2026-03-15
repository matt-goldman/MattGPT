using System.Text.Json;

namespace MattGPT.AppHost;

/// <summary>
/// Sets up the Azure App Configuration resource and (in run mode) the emulator + ConfigSeeder.
/// </summary>
internal static class AppHostConfiguration
{
    /// <summary>Configuration keys that are seeded into the App Configuration emulator.</summary>
    private static readonly string[] SeedKeys =
    [
        "Auth:Enabled", "Auth:Provider",
        "LLM:Provider", "LLM:ModelId", "LLM:EmbeddingModelId",
        "LLM:Endpoint", "LLM:ApiKey",
        "LLM:EmbeddingProvider", "LLM:EmbeddingApiKey", "LLM:EmbeddingEndpoint",
        "RAG:Mode",
        "DocumentDb:Provider",
        "VectorStore:Provider", "VectorStore:Endpoint",
        "VectorStore:ApiKey", "VectorStore:IndexName",
    ];

    internal static (IResourceBuilder<AzureAppConfigurationResource> AppConfig, IResourceBuilder<ProjectResource>? ConfigSeeder)
        AddAppConfiguration(this IDistributedApplicationBuilder builder)
    {
        var appConfig = builder.AddAzureAppConfiguration("appconfig");

        IResourceBuilder<ProjectResource>? configSeeder = null;
        if (builder.ExecutionContext.IsRunMode)
        {
            appConfig.RunAsEmulator(emulator => emulator.WithDataVolume());

            // Collect seed-eligible values into a single JSON dictionary so the
            // ConfigSeeder project doesn't need its own copy of the key list.
            var seedValues = new Dictionary<string, string>();
            foreach (var key in SeedKeys)
            {
                var value = builder.Configuration[key];
                if (!string.IsNullOrEmpty(value))
                    seedValues[key] = value;
            }

            configSeeder = builder.AddProject<Projects.MattGPT_ConfigSeeder>("configseeder")
                .WithHttpHealthCheck("/health")
                .WithReference(appConfig)
                .WaitFor(appConfig)
                .WithEnvironment("Seed__Json", JsonSerializer.Serialize(seedValues));
        }

        return (appConfig, configSeeder);
    }
}
