using CommunityToolkit.Maui;
using MattGPT.ApiClient;
using MattGPT.Mobile.Auth;
using MattGPT.Mobile.Popups;
using MattGPT.Mobile.Services;
using MattGPT.Mobile.ViewModels;
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
            .UseMauiCommunityToolkit(static options => options.SetPopupDefaults(new DefaultPopupSettings { CanBeDismissedByTappingOutsideOfPopup = false }));

		// Register auth services
		builder.Services.AddSingleton<MobileAuthService>();

		builder.Services.AddApiClient<AuthDelegatingHandler>(new Uri("https+http://apiservice"));

		builder.Services.AddTransientPopup<AuthPopup, AuthViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
