using CommunityToolkit.Maui;
using MattGPT.ApiClient;
using MattGPT.Mobile.Auth;
using MattGPT.Mobile.Services;
using Microsoft.Extensions.Logging;
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
			.UseAutodependencies()
            .UseMauiCommunityToolkit();

		// Register auth services
		builder.Services.AddSingleton<MobileAuthService>();
		builder.Services.AddTransient<AuthDelegatingHandler>();

		builder.Services.AddMattGptApiClient(new Uri("https+http://apiservice"))
			.AddHttpMessageHandler<AuthDelegatingHandler>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
