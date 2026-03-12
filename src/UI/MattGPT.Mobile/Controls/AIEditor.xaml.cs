using Plugin.Maui.Lucide;
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
		BindingMode.TwoWay);
	public string Prompt
	{
		get => (string)GetValue(PromptProperty);
		set => SetValue(PromptProperty, value);
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
            EndAction.Text = Icons.ArrowBigUp;
        }

		Prompt = e.NewTextValue;
    }

    private void EndAction_Clicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(Prompt) && !_isListening)
		{
			_isListening = true;
			EndAction.Text = Icons.Square;

            // TODO: start CommunityToolkit STT
        }
        else if (_isListening)
		{
			_isListening = false;
			EndAction.Text = string.IsNullOrEmpty(Prompt)?  Icons.AudioLines : Icons.ArrowBigUp;
        }
		else
		{
			SendCommand?.Execute(Prompt);
            Prompt = string.Empty;
			InputEditor.Text = string.Empty;
        }
    }
}