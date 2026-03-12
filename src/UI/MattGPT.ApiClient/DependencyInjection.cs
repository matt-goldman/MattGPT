using MattGPT.ApiClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MattGPT.ApiClient;

/// <summary>Shared defaults used across the ApiClient library.</summary>
internal static class MattGptApiClientDefaults
{
    /// <summary>Named <see cref="System.Net.Http.HttpClient"/> key used by all ApiClient services.</summary>
    internal const string ClientName = "mattgpt-api";
}

/// <summary>
/// Extension methods for registering MattGPT API client services.
/// </summary>
public static class MattGptApiClientExtensions
{
    /// <summary>
    /// Adds the MattGPT API client services and registers a named <see cref="System.Net.Http.HttpClient"/>
    /// pointed at <paramref name="baseAddress"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="baseAddress">The base address of the MattGPT API service.</param>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> for the underlying named client, allowing callers
    /// to add delegating handlers (e.g. authentication) if required.
    /// </returns>
    public static IHttpClientBuilder AddMattGptApiClient(this IServiceCollection services, Uri baseAddress)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ISettingsService, SettingsService>();

        return services.AddHttpClient(MattGptApiClientDefaults.ClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(10);
        });
    }

    /// <summary>
    /// Adds the MattGPT API client services and registers a named <see cref="System.Net.Http.HttpClient"/>
    /// with a custom <see cref="DelegatingHandler"/>.
    /// </summary>
    /// <typeparam name="THandler">The type of the custom <see cref="DelegatingHandler"/>.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="baseAddress">The base address of the MattGPT API service.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddApiClient<THandler>(this IServiceCollection services, Uri baseAddress)
        where THandler : DelegatingHandler
    {
        services.AddTransient<THandler>();

        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddHttpClient(MattGptApiClientDefaults.ClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromMinutes(10);
        })
        .AddHttpMessageHandler<THandler>();

        return services;
    }
}
