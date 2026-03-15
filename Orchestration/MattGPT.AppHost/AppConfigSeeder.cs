using System.Net;
using System.Text;
using System.Text.Json;
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
        // The Aspire emulator has HMAC-SHA256 disabled and uses Anonymous auth over plain
        // HTTP. The ConfigurationClient(connectionString) constructor forces HMAC-SHA256,
        // and the SDK blocks Bearer tokens over non-TLS endpoints. For the emulator we
        // must use raw HTTP calls with anonymous access instead.
        if (TryGetEmulatorEndpoint(connectionString, out var endpoint))
        {
            await SeedViaHttpAsync(endpoint, configuration, cancellationToken);
        }
        else
        {
            await SeedViaClientAsync(connectionString, configuration, cancellationToken);
        }
    }

    /// <summary>
    /// Checks whether the connection string targets the local emulator (Anonymous=True)
    /// and extracts the endpoint URI.
    /// </summary>
    private static bool TryGetEmulatorEndpoint(string connectionString, out Uri endpoint)
    {
        endpoint = null!;
        string? endpointValue = null;
        bool isAnonymous = false;

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;

            var key = segment[..eq].Trim();
            var val = segment[(eq + 1)..].Trim();

            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                endpointValue = val;
            else if (key.Equals("Anonymous", StringComparison.OrdinalIgnoreCase)
                     && val.Equals("True", StringComparison.OrdinalIgnoreCase))
                isAnonymous = true;
        }

        if (isAnonymous && endpointValue is not null)
        {
            endpoint = new Uri(endpointValue);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Seeds the emulator via anonymous HTTP/1.1 calls to the App Configuration REST API.
    /// </summary>
    private static async Task SeedViaHttpAsync(
        Uri endpoint,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = endpoint };

        foreach (var key in SeededKeys)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
                continue;

            var json = JsonSerializer.Serialize(new { value });
            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"/kv/{Uri.EscapeDataString(key)}?api-version=2023-11-01")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/vnd.microsoft.appconfig.kv+json"),
                Version = HttpVersion.Version11 // emulator is HTTP/1.1
            };
            // If-None-Match: * → only create if the key does not already exist (set-if-not-exists).
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                continue; // Key already exists — leave it alone.

            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Seeds a real Azure App Configuration store using the SDK with HMAC-SHA256 auth.
    /// </summary>
    private static async Task SeedViaClientAsync(
        string connectionString,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var client = new ConfigurationClient(connectionString);

        foreach (var key in SeededKeys)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
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
