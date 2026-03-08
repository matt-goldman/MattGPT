using MattGPT.Mobile.ViewModels;

namespace MattGPT.Mobile.Pages;

public partial class ChatPage : ContentPage
{
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}