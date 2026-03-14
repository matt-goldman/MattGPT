using CommunityToolkit.Maui;
using MattGPT.Mobile.Services;
using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile;

public partial class AppShell : Shell
{
    private readonly IPopupService popupService;
    private readonly NetCoreIdAuthService authService;

    public AppShell(
        IPopupService popupService,
        NetCoreIdAuthService authService)
	{
		InitializeComponent();
        this.popupService = popupService;
        this.authService = authService;
    }

	protected override async void OnAppearing()
	{
		base.OnAppearing();

        var accessToken = await authService.GetAccessTokenAsync();
        
        if (accessToken is null)
        {
            await popupService.ShowPopupAsync<AuthViewModel>(this);
        }
    }
}
