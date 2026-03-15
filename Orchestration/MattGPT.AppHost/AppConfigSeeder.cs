using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;

#pragma warning disable IDE0130 // Namespace does not match folder structure - AppHost uses top-level statements, so the namespace is not expected to match the folder structure.
namespace Aspire.Hosting;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Seeds an Azure App Configuration store (or local emulator) with application-level
/// settings read from <see cref="IConfiguration"/>. Uses set-if-not-exists semantics so
/// that developer customisations already present in the store are never overwritten.
/// </summary>
internal static class AppConfigSeeder
{
    /// <summary>
    /// The configuration keys that are eligible for seeding. Values are read from
    /// <paramref name="configuration"/>. Keys whose resolved value is <see langword="null"/>
    /// or empty are skipped.
    /// </summary>
    private static readonly string[] SeededKeys =
    [
        "Auth:Enabled",
        "Auth:Provider",
        "LLM:Provider",
        "LLM:ModelId",
        "LLM:EmbeddingModelId",
        "LLM:Endpoint",
        "LLM:ApiKey",
        "LLM:EmbeddingProvider",
        "LLM:EmbeddingApiKey",
        "LLM:EmbeddingEndpoint",
        "RAG:Mode",
        "DocumentDb:Provider",
        "VectorStore:Provider",
        "VectorStore:Endpoint",
        "VectorStore:ApiKey",
        "VectorStore:IndexName",
    ];

    /// <summary>
    /// Seeds the App Configuration store identified by <paramref name="connectionString"/>
    /// with values from <paramref name="configuration"/>. Only keys listed in
    /// <see cref="SeededKeys"/> are considered. Keys that already exist in the store are
    /// left unchanged.
    /// </summary>
    internal static async Task SeedAsync(
        string connectionString,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var client = new ConfigurationClient(connectionString);

        foreach (var key in SeededKeys)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                // Only add the setting if it does not already exist.
                await client.AddConfigurationSettingAsync(
                    new ConfigurationSetting(key, value),
                    cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // HTTP 412 Precondition Failed means the key already exists — leave it alone.
            }
        }
    }
}
