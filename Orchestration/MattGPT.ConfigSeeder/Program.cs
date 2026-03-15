using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Track seeding state so the health check can report progress.
// Other Aspire resources WaitFor this service, so they won't start until the
// health check returns Healthy (i.e. seeding is complete).
var seedState = new SeedingState();
builder.Services.AddSingleton(seedState);
builder.Services.AddHealthChecks()
    .AddCheck("seeding", () => seedState.IsComplete
        ? HealthCheckResult.Healthy("Seeding complete.")
        : HealthCheckResult.Unhealthy("Seeding in progress."));

builder.Services.AddHostedService<ConfigSeedingService>();

var app = builder.Build();
app.MapDefaultEndpoints();
await app.RunAsync();

// ---------------------------------------------------------------------------
// Supporting types
// ---------------------------------------------------------------------------

/// <summary>Tracks whether the one-shot seeding work has finished.</summary>
sealed class SeedingState
{
    public bool IsComplete { get; set; }
}

/// <summary>
/// Background service that seeds Azure App Configuration with application-level defaults.
/// Values to seed are passed by the AppHost as environment variables under the <c>Seed:</c>
/// configuration prefix (e.g. <c>Seed__LLM__Provider</c>). The service writes them into
/// the App Configuration store using set-if-not-exists semantics so that any values the
/// developer has already customised are never overwritten.
/// </summary>
sealed class ConfigSeedingService(
    IConfiguration configuration,
    SeedingState state,
    ILogger<ConfigSeedingService> logger) : BackgroundService
{
    /// <summary>App Configuration key names eligible for seeding.</summary>
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("appconfig");
        if (string.IsNullOrEmpty(connectionString))
        {
            logger.LogWarning("No 'appconfig' connection string found. Skipping config seeding.");
            state.IsComplete = true;
            return;
        }

        try
        {
            if (TryGetEmulatorEndpoint(connectionString, out var endpoint))
            {
                await SeedViaHttpAsync(endpoint, stoppingToken);
            }
            else
            {
                await SeedViaClientAsync(connectionString, stoppingToken);
            }

            logger.LogInformation("App Configuration seeding complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "App Configuration seeding failed.");
        }
        finally
        {
            state.IsComplete = true;
        }
    }

    // ------------------------------------------------------------------
    // Emulator detection
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when the connection string contains
    /// <c>Anonymous=True</c>, indicating the Aspire App Configuration emulator.
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

    // ------------------------------------------------------------------
    // Emulator path — anonymous HTTP/1.1
    // ------------------------------------------------------------------

    /// <summary>
    /// Seeds the emulator via anonymous HTTP/1.1 calls to the App Configuration REST API.
    /// The emulator has HMAC-SHA256 disabled and the Azure SDK blocks Bearer over HTTP,
    /// so raw HTTP is the only viable approach for the emulator.
    /// </summary>
    private async Task SeedViaHttpAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = endpoint };

        foreach (var key in SeededKeys)
        {
            var value = configuration[$"Seed:{key}"];
            if (string.IsNullOrEmpty(value))
                continue;

            var json = JsonSerializer.Serialize(new { value });
            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"/kv/{Uri.EscapeDataString(key)}?api-version=2023-11-01")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/vnd.microsoft.appconfig.kv+json"),
                Version = HttpVersion.Version11 // emulator only supports HTTP/1.1
            };
            // If-None-Match: * → only create if key does not already exist.
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                logger.LogDebug("Key '{Key}' already exists; skipping.", key);
                continue;
            }

            response.EnsureSuccessStatusCode();
            logger.LogDebug("Seeded key '{Key}'.", key);
        }
    }

    // ------------------------------------------------------------------
    // Production path — Azure SDK with HMAC-SHA256
    // ------------------------------------------------------------------

    /// <summary>
    /// Seeds a real Azure App Configuration store using the SDK (HMAC-SHA256 auth, HTTPS).
    /// </summary>
    private async Task SeedViaClientAsync(string connectionString, CancellationToken cancellationToken)
    {
        var client = new ConfigurationClient(connectionString);

        foreach (var key in SeededKeys)
        {
            var value = configuration[$"Seed:{key}"];
            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                await client.AddConfigurationSettingAsync(
                    new ConfigurationSetting(key, value),
                    cancellationToken);
                logger.LogDebug("Seeded key '{Key}'.", key);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogDebug("Key '{Key}' already exists; skipping.", key);
            }
        }
    }
}
