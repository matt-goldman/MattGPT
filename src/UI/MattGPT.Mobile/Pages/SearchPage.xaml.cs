using MattGPT.UI.ViewModels;

namespace MattGPT.UI.Pages;

public partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}