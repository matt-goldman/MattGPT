using CommunityToolkit.Maui;
using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile;

public partial class AppShell : Shell
{
	private readonly IPopupService _popupService;
	private bool _hasAttemptedAuth = false;

	public AppShell(IPopupService popupService)
	{
		InitializeComponent();
		_popupService = popupService;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (!_hasAttemptedAuth)
		{
			_hasAttemptedAuth = true;
			await _popupService.ShowPopupAsync<AuthViewModel>(this);
		}
	}
}
