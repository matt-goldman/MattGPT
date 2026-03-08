using CommunityToolkit.Maui;
using MattGPT.ApiClient;
using MattGPT.UI.Auth;
using MattGPT.UI.Services;
using Microsoft.Extensions.Logging;
using Plugin.Maui.SmartNavigation.Attributes;

namespace MattGPT.UI;

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
			.UseAutodependencies()
            .UseMauiCommunityToolkit();

		var apiBaseUrl = "http://localhost:5000"; // TODO: replace with Aspire variable

		// Register auth services
		builder.Services.AddSingleton<MobileAuthService>();
		builder.Services.AddTransient<AuthDelegatingHandler>();

		// Register MattGPT API client.
		// Override ApiBaseUrl in appsettings.json to point to your deployed API service.
		// For Android emulator connecting to the host, use http://10.0.2.2:<port>.
		builder.Services.AddMattGptApiClient(new Uri(apiBaseUrl))
			.AddHttpMessageHandler<AuthDelegatingHandler>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
