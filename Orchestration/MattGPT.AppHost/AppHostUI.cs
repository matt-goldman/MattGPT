namespace MattGPT.AppHost;

/// <summary>
/// Sets up the web frontend, MAUI mobile app, and dev tunnels.
/// </summary>
internal static class AppHostUI
{
    internal static void AddUI(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzureAppConfigurationResource> appConfig,
        IResourceBuilder<ProjectResource>? configSeeder,
        IResourceBuilder<ProjectResource> apiService,
        InfraResources infra)
    {
        // --- Web frontend ---
        var webfrontend = builder.AddProject<Projects.MattGPT_Web>("webfrontend")
            .WithExternalHttpEndpoints()
            .WithHttpHealthCheck("/health")
            .WithReference(appConfig)
            .WaitFor(appConfig)
            .WithReference(apiService)
            .WaitFor(apiService);

        if (configSeeder is not null)
            webfrontend.WaitFor(configSeeder);

        if (infra.Keycloak is not null)
        {
            webfrontend.WithReference(infra.Keycloak).WaitFor(infra.Keycloak);
        }

        // --- Dev tunnel for secure external access to the API ---
        var tunnel = builder.AddDevTunnel("tunnel")
            .WaitFor(apiService)
            .WithAnonymousAccess()
            .WithReference(apiService.GetEndpoint("https"));

        // --- MAUI mobile app ---
        var mauiapp = builder.AddMauiProject("mauiapp", @"../../src/UI/MattGPT.Mobile/MattGPT.Mobile.csproj");

        mauiapp.AddWindowsDevice()
            .WaitFor(apiService)
            .WithReference(apiService);

        mauiapp.AddMacCatalystDevice()
            .WaitFor(apiService)
            .WithReference(apiService);

        var ios = mauiapp.AddiOSSimulator()
            .WaitFor(apiService)
            .WithOtlpDevTunnel()
            .WithReference(apiService, tunnel);

        var android = mauiapp.AddAndroidEmulator()
            .WaitFor(apiService)
            .WithOtlpDevTunnel()
            .WithReference(apiService, tunnel);

        // --- Keycloak tunnel for mobile devices ---
        if (infra.Keycloak is not null)
        {
            var kcTunnel = builder.AddDevTunnel("kcTunnel")
                .WithAnonymousAccess()
                .WithReference(infra.Keycloak);

            ios.WithReference(infra.Keycloak, kcTunnel);
            android.WithReference(infra.Keycloak, kcTunnel);
        }
    }
}
