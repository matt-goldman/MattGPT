using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile.Popups;

public partial class AuthPopup : ContentView
{
	public AuthPopup(AuthViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}