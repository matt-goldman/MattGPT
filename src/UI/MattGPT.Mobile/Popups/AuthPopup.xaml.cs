using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile.Popups;

public partial class AuthPopup : ContentView
{
	public AuthPopup(AuthViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
		Loaded += async (s, e) => await viewModel.InitializeCommand.ExecuteAsync(null);
	}
}
