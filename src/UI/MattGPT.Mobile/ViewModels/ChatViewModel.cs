using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MattGPT.ApiClient.Models;
using MattGPT.ApiClient.Services;
using System.Collections.ObjectModel;

namespace MattGPT.Mobile.ViewModels;

public partial class ChatViewModel(IChatService chatService) : ObservableObject
{
    private Guid? _sessionId;
    private CancellationTokenSource? _streamCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _userInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isStreaming;

    [ObservableProperty]
    private string _sessionTitle = "New Chat";

    [ObservableProperty]
    private string _toolStatusMessage = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        var message = UserInput.Trim();
        if (string.IsNullOrEmpty(message)) return;

        UserInput = string.Empty;
        IsStreaming = true;
        ToolStatusMessage = string.Empty;

        var userMessage = new ChatMessage { Role = "user", Content = message, Timestamp = DateTimeOffset.Now };
        var assistantMessage = new ChatMessage { Role = "assistant", Content = string.Empty, Timestamp = DateTimeOffset.Now };

        Messages.Add(userMessage);
        Messages.Add(assistantMessage);

        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = new CancellationTokenSource();
        try
        {
            await foreach (var evt in chatService.StreamChatAsync(message, _sessionId, _streamCts.Token))
            {
                switch (evt)
                {
                    case SessionChatEvent sessionEvt:
                        _sessionId = sessionEvt.SessionId;
                        break;

                    case TokenChatEvent tokenEvt:
                        MainThread.BeginInvokeOnMainThread(() =>
                            assistantMessage.Content += tokenEvt.Token);
                        break;

                    case ToolStartChatEvent:
                        MainThread.BeginInvokeOnMainThread(() =>
                            ToolStatusMessage = "Searching memories…");
                        break;

                    case ToolEndChatEvent:
                        MainThread.BeginInvokeOnMainThread(() =>
                            ToolStatusMessage = string.Empty);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                assistantMessage.Content = $"Error: {ex.Message}");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsStreaming = false;
                ToolStatusMessage = string.Empty;
            });
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private bool CanSend() => !IsStreaming && !string.IsNullOrWhiteSpace(UserInput);
}
