using Microsoft.Extensions.Configuration;

namespace MattGPT.AppHost;

internal static class ConfigurationExtensions
{
    /// <summary>
    /// Returns the configuration value for <paramref name="key"/>, treating
    /// <c>null</c>, empty, and whitespace-only values as missing and returning
    /// <paramref name="defaultValue"/> instead.
    /// </summary>
    /// <remarks>
    /// Placeholder entries in <c>appsettings.json</c> (e.g. <c>"Provider": ""</c>)
    /// are useful to signal to operators which keys are expected, but they would
    /// otherwise defeat <c>??</c>-style fallbacks because the configuration system
    /// returns the empty string rather than <c>null</c>.
    /// </remarks>
    public static string GetValueOrDefault(this IConfiguration configuration, string key, string defaultValue)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
