using Azure;
using Azure.Data.AppConfiguration;

namespace Aspire.Hosting;

/// <summary>
/// Seeds an Azure App Configuration store (or local emulator) with a set of key-value pairs.
/// Uses set-if-not-exists semantics so that any values already present in the store — for
/// example because a developer has customised them — are never overwritten.
/// </summary>
internal static class AppConfigSeeder
{
    /// <summary>
    /// Seeds <paramref name="values"/> into the App Configuration store identified by
    /// <paramref name="connectionString"/>. Keys whose value is <see langword="null"/> or
    /// empty are skipped. Keys that already exist in the store are left unchanged.
    /// </summary>
    internal static async Task SeedAsync(
        string connectionString,
        Dictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        var client = new ConfigurationClient(connectionString);

        foreach (var (key, value) in values)
        {
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
