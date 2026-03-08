using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile.Pages;

public partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}