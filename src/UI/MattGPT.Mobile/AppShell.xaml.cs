using CommunityToolkit.Maui;
using MattGPT.Mobile.Services;
using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile;

public partial class AppShell : Shell
{
    private readonly KeycloakAuthService? _keycloakAuth;
    private readonly IPopupService? _popupService;
    private readonly NetCoreIdAuthService? _legacyAuth;

    /// <summary>Keycloak auth path — login is handled via system browser.</summary>
    public AppShell(KeycloakAuthService keycloakAuth)
	{
		InitializeComponent();
        _keycloakAuth = keycloakAuth;
    }

    /// <summary>Legacy Identity auth path — login is handled via in-app popup.</summary>
    public AppShell(IPopupService popupService, NetCoreIdAuthService legacyAuth)
    {
        InitializeComponent();
        _popupService = popupService;
        _legacyAuth = legacyAuth;
    }

	protected override async void OnAppearing()
	{
		base.OnAppearing();

        if (_keycloakAuth is not null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var token = await _keycloakAuth.GetAccessTokenAsync();
                if (token is null)
                {
                    var loginSucceeded = await _keycloakAuth.LoginAsync();
                    if (!loginSucceeded && _popupService is not null)
                    {
                        // TODO: replace with a custom popup
                        await DisplayAlertAsync("Authentication Failed", "Unable to authenticate via Keycloak. Please try again.", "OK");
                    }
                }
            });
        }
        else if (_legacyAuth is not null)
        {
            var token = await _legacyAuth.GetAccessTokenAsync();
            if (token is null && _popupService is not null)
            {
                await _popupService.ShowPopupAsync<AuthViewModel>(this);
            }
        }
    }
}
