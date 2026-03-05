using MattGPT.UI.ViewModels;

namespace MattGPT.UI.Pages;

public partial class ChatPage : ContentPage
{
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}