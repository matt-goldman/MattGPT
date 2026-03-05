using CommunityToolkit.Maui;
using MattGPT.ApiClient;
using MattGPT.UI.Auth;
using MattGPT.UI.Services;
using Microsoft.Extensions.Configuration;
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

		// Load configuration from the embedded appsettings.json.
		var assembly = typeof(MauiProgram).Assembly;
		using var configStream = assembly.GetManifestResourceStream("MattGPT.UI.appsettings.json");
		if (configStream is not null)
			builder.Configuration.AddJsonStream(configStream);

		var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";

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
