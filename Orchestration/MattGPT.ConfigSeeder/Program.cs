using System.Net;
using System.Text;
using System.Text.Json;
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
/// Background service that seeds the Azure App Configuration emulator with
/// application-level defaults. The AppHost passes a JSON dictionary of key/value
/// pairs via the <c>Seed__Json</c> environment variable. The service writes them
/// into the emulator using set-if-not-exists semantics so that any values the
/// developer has already customised are never overwritten.
/// </summary>
sealed class ConfigSeedingService(
    IConfiguration configuration,
    SeedingState state,
    ILogger<ConfigSeedingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("appconfig");
            if (string.IsNullOrEmpty(connectionString))
            {
                logger.LogWarning("No 'appconfig' connection string found. Skipping config seeding.");
                return;
            }

            var seedJson = configuration["Seed:Json"];
            if (string.IsNullOrEmpty(seedJson))
            {
                logger.LogWarning("No 'Seed:Json' value found. Nothing to seed.");
                return;
            }

            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(seedJson);
            if (settings is null || settings.Count == 0)
            {
                logger.LogWarning("Seed:Json deserialized to empty dictionary. Nothing to seed.");
                return;
            }

            var endpoint = GetEndpointFromConnectionString(connectionString);
            await SeedAsync(endpoint, settings, stoppingToken);

            logger.LogInformation("App Configuration emulator seeding complete ({Count} keys processed).", settings.Count);
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

    /// <summary>
    /// Extracts the Endpoint URI from the emulator connection string.
    /// </summary>
    private static Uri GetEndpointFromConnectionString(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0) continue;

            var key = segment[..eq].Trim();
            var val = segment[(eq + 1)..].Trim();

            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                return new Uri(val);
        }

        throw new InvalidOperationException("Connection string does not contain an 'Endpoint' value.");
    }

    /// <summary>
    /// Seeds the emulator via anonymous HTTP/1.1 calls to the App Configuration REST API.
    /// The emulator has HMAC-SHA256 disabled and the Azure SDK blocks Bearer over HTTP,
    /// so raw HTTP is the only viable approach.
    /// </summary>
    private async Task SeedAsync(
        Uri endpoint,
        Dictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = endpoint };

        foreach (var (key, value) in settings)
        {
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
}
