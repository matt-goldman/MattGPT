using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Media;
using Plugin.Maui.Lucide;
using System.Globalization;
using System.Windows.Input;
namespace MattGPT.Mobile.Controls;

// TODO: move this to FlagstoneUI
public partial class AIEditor : ContentView
{
	public static readonly BindableProperty PromptProperty = BindableProperty.Create(
		nameof(Prompt),
		typeof(String),
		typeof(AIEditor),
		string.Empty,
		BindingMode.TwoWay,
		propertyChanged: OnPromptChanged);
	public string Prompt
	{
		get => (string?)GetValue(PromptProperty) ?? string.Empty;
		set => SetValue(PromptProperty, value ?? string.Empty);
	}

	private static void OnPromptChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is AIEditor editor && editor.InputEditor is not null)
		{
			var newText = (string?)newValue ?? string.Empty;
			if (editor.InputEditor.Text != newText)
				editor.InputEditor.Text = newText;
		}
	}

	public static readonly BindableProperty SendCommandProperty = BindableProperty.Create(
		nameof(SendCommand),
		typeof(ICommand),
		typeof(AIEditor),
		defaultBindingMode: BindingMode.TwoWay);
    public ICommand SendCommand
	{
		get => (ICommand)GetValue(SendCommandProperty);
		set => SetValue(SendCommandProperty, value);
    }

    private bool _isListening = false;
    private ISpeechToText? _activeSpeechService;
    private CancellationTokenSource? _speechCts;

	public AIEditor()
	{
		InitializeComponent();
	}

	protected override void OnPropertyChanged(string propertyName = null)
	{
		base.OnPropertyChanged(propertyName);

		if (string.IsNullOrEmpty(propertyName) || propertyName == nameof(IsEnabled))
		{
			var isEnabled = IsEnabled;
			if (InputEditor is not null)
			{
				InputEditor.IsEnabled = isEnabled;
			}
			if (EndAction is not null)
			{
				EndAction.IsEnabled = isEnabled;
			}
		}
	}

    private void InputEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
		if (_isListening) return;

		if (string.IsNullOrEmpty(e.NewTextValue))
		{
			EndAction.Text = Icons.AudioLines;
		}
		else
		{
            EndAction.Text = Icons.ArrowBigUp;
        }

		Prompt = e.NewTextValue;
    }

    private async void EndAction_Clicked(object sender, EventArgs e)
    {
        if (_isListening)
        {
            await StopListeningAsync();
        }
        else if (string.IsNullOrEmpty(Prompt))
        {
            await StartListeningAsync();
        }
        else
        {
            SendCommand?.Execute(Prompt);
            Prompt = string.Empty;
            InputEditor.Text = string.Empty;
        }
    }

    private async Task StartListeningAsync()
    {
        var speechService = await AIEditor.ResolveSpeechServiceAsync();
        if (speechService is null) return;

        _activeSpeechService = speechService;
        _activeSpeechService.RecognitionResultUpdated += OnRecognitionResultUpdated;
        _activeSpeechService.RecognitionResultCompleted += OnRecognitionResultCompleted;

        _isListening = true;
        EndAction.Text = Icons.Square;

        _speechCts = new CancellationTokenSource();
        await _activeSpeechService.StartListenAsync(
            new SpeechToTextOptions { Culture = CultureInfo.CurrentCulture, ShouldReportPartialResults = true },
            _speechCts.Token);
    }

    private async Task StopListeningAsync()
    {
        if (_activeSpeechService is null) return;
        await _activeSpeechService.StopListenAsync(CancellationToken.None);
        // Cleanup happens in OnRecognitionResultCompleted once the service fires the final result
    }

    private static async Task<ISpeechToText?> ResolveSpeechServiceAsync()
    {
        if (Connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            var granted = await RequestSpeechPermissionsAsync(SpeechToText.Default);
            return granted ? SpeechToText.Default : null;
        }

        // No network — try offline recognition
        try
        {
            var granted = await RequestSpeechPermissionsAsync(OfflineSpeechToText.Default);
            return granted ? OfflineSpeechToText.Default : null;
        }
        catch (Exception)
        {
            await Toast.Make("Speech recognition requires an internet connection on this device.").Show(CancellationToken.None);
            return null;
        }
    }

    private static async Task<bool> RequestSpeechPermissionsAsync(ISpeechToText speechService)
    {
        var micStatus = await Permissions.RequestAsync<Permissions.Microphone>();
        if (micStatus != PermissionStatus.Granted)
            return false;

        return await speechService.RequestPermissions(CancellationToken.None);
    }

    private void OnRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e)
    {
        // Partial result — update prompt so the user sees live transcription.
        // OnPromptChanged propagates the value to InputEditor.Text automatically.
        Prompt = e.RecognitionResult;
    }

    private void OnRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
    {
        Prompt = e.RecognitionResult.IsSuccessful ? e.RecognitionResult.Text : Prompt;
        CleanupSpeechService();
    }

    private void CleanupSpeechService()
    {
        if (_activeSpeechService is not null)
        {
            _activeSpeechService.RecognitionResultUpdated -= OnRecognitionResultUpdated;
            _activeSpeechService.RecognitionResultCompleted -= OnRecognitionResultCompleted;
            _activeSpeechService = null;
        }

        _speechCts?.Dispose();
        _speechCts = null;
        _isListening = false;
        EndAction.Text = string.IsNullOrEmpty(Prompt) ? Icons.AudioLines : Icons.ArrowBigUp;
    }
}