using Plugin.Maui.Lucide;
using System.Windows.Input;
namespace MattGPT.Mobile.Controls;

// TODO: move this to FlagstoneUI
public partial class AIEditor : ContentView
{
	public static readonly BindableProperty PromptPropterty = BindableProperty.Create(
		nameof(Prompt),
		typeof(String),
		typeof(AIEditor),
		string.Empty);
    public string Prompt
	{
		get => (string)GetValue(PromptPropterty);
		set => SetValue(PromptPropterty, value);
	}

	public static readonly BindableProperty SendCommandProperty = BindableProperty.Create(
		nameof(SendCommand),
		typeof(ICommand),
		typeof(AIEditor));
    public ICommand SendCommand
	{
		get => (ICommand)GetValue(SendCommandProperty);
		set => SetValue(SendCommandProperty, value);
    }

	public static readonly new BindableProperty IsEnabledProperty = BindableProperty.Create(
		nameof(IsEnabled),
		typeof(bool),
		typeof(AIEditor),
		true,
		propertyChanged: IsEnabledChanged);

    private static void IsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
		if (bindable is AIEditor editor)
		{
			bool isEnabled = (bool)newValue;
			editor.InputEditor.IsEnabled = isEnabled;
			editor.EndAction.IsEnabled = isEnabled;
        }
    }

    public new bool IsEnabled
	{
		get => (bool)GetValue(IsEnabledProperty);
		set => SetValue(IsEnabledProperty, value);
	}

    private bool _isListening = false;

    public AIEditor()
	{
		InitializeComponent();
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
            EndAction.Text = Icons.AArrowUp;
			Prompt = e.NewTextValue;
        }
    }

    private void EndAction_Clicked(object sender, EventArgs e)
    {
		if (string.IsNullOrEmpty(Prompt))
		{
			_isListening = true;
			EndAction.Text = Icons.Square;

			// TODO: Community Toolkit STT
        }
		else if (_isListening)
		{
			_isListening = false;
			EndAction.Text = string.IsNullOrEmpty(Prompt)?  Icons.AudioLines : Icons.AArrowUp;
        }
		else
		{
			SendCommand?.Execute(Prompt);
			Prompt = string.Empty;
			InputEditor.Text = string.Empty;
        }
    }
}