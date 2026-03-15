using CommunityToolkit.Maui;
using Duende.IdentityModel.OidcClient;
using MattGPT.ApiClient;
using MattGPT.ApiClient.Services;
using MattGPT.Mobile.Auth;
using MattGPT.Mobile.Popups;
using MattGPT.Mobile.Services;
using MattGPT.Mobile.ViewModels;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Lucide;
using Plugin.Maui.SmartNavigation.Attributes;

namespace MattGPT.Mobile;

[UseAutoDependencies]
public static partial class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
            .UseLucide()
			.UseAutodependencies()
            .UseMauiCommunityToolkit(static options => options.SetPopupDefaults(new DefaultPopupSettings { CanBeDismissedByTappingOutsideOfPopup = false }));

		// Register auth services
		
        // currently unable to get config from Aspire, see: https://github.com/jfversluis/MauiAspire/issues/7#issuecomment-4037843834
        // in the meantime, specify this manually to control whether the app uses Keycloak auth or not
        var useKeycloakAuth = true; // get this from Aspire when the issue is resolved

		if (useKeycloakAuth)
		{
            var oidcClient = new OidcClient(new()
            {
                // TODO: resolve Keycloak authority from Aspire service discovery when the MauiAspire issue is fixed
                // For now, use the realm URL from the local Keycloak instance (via Aspire AppHost)
                Authority   = "https://jvv8rkv2-62973.aue.devtunnels.ms/realms/mattgpt",
                ClientId    = "mattgpt-mobile",
                Scope       = "openid profile email",
                RedirectUri = "mattgpt://callback",
                Browser     = new MobileAuthBrowser(),
                DisablePushedAuthorization = true
            });

#if DEBUG
            // Dev tunnel doesn't use host header forwarding, so issuer name doesn't match authority.
            // Disable issuer name validation for debug only.
            oidcClient.Options.Policy = new Policy
            {
                ValidateTokenIssuerName = false,
                Discovery = new Duende.IdentityModel.Client.DiscoveryPolicy
                {
                    ValidateEndpoints = false,
                    ValidateIssuerName = false,
                }
            };
#endif

            builder.Services.AddSingleton(oidcClient);

            builder.Services.AddSingleton<KeycloakAuthService>();
            builder.Services.AddSingleton<IAuthFailureHandler, KeycloakAuthFailureHandler>();

            // same Aspire issue mentioned above, so hardcoding the API base address for now
            builder.Services.AddApiClient<KeycloakAuthDelegatingHandler, KeycloakAuthFailureHandler>(
                new Uri("https://gqb8jt03-7321.aue.devtunnels.ms"));

            builder.Services.AddSingleton<AppShell>(sp =>
                new AppShell(sp.GetRequiredService<KeycloakAuthService>()));
        }
		else
		{
            builder.Services.AddSingleton<NetCoreIdAuthService>();

            // same Aspire issue mentioned above, so hardcoding the API base address for now
            builder.Services.AddApiClient<AuthDelegatingHandler>(
                new Uri("https://gqb8jt03-7321.aue.devtunnels.ms")); // "https+http://apiservice"

            builder.Services.AddTransientPopup<AuthPopup, AuthViewModel>();

            builder.Services.AddSingleton<AppShell>(sp =>
                new AppShell(
                    sp.GetRequiredService<IPopupService>(),
                    sp.GetRequiredService<NetCoreIdAuthService>()));
        }

#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
