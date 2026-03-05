using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MattGPT.ApiClient.Services;

namespace MattGPT.UI.ViewModels;

public partial class SettingsViewModel(ISettingsService settingsService) : ObservableObject
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    private string? _systemPrompt;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private string? _userProfileText;

    [ObservableProperty]
    private string? _userInstructions;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var promptTask = settingsService.GetSystemPromptAsync();
            var profileTask = settingsService.GetUserProfileAsync();
            await Task.WhenAll(promptTask, profileTask);

            var prompt = await promptTask;
            if (prompt is not null)
            {
                SystemPrompt = prompt.SystemPrompt;
                IsDefault = prompt.IsDefault;
            }

            var profile = await profileTask;
            if (profile is not null)
            {
                UserProfileText = profile.UserProfileText;
                UserInstructions = profile.UserInstructions;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        StatusMessage = string.Empty;

        try
        {
            var promptTask = settingsService.SaveSystemPromptAsync(SystemPrompt);
            var profileTask = settingsService.SaveUserProfileAsync(UserProfileText, UserInstructions);
            await Task.WhenAll(promptTask, profileTask);

            // Reload to get the authoritative IsDefault value from the server.
            var updated = await settingsService.GetSystemPromptAsync();
            if (updated is not null)
                IsDefault = updated.IsDefault;

            StatusMessage = "Settings saved successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetSystemPromptAsync()
    {
        StatusMessage = string.Empty;

        try
        {
            var result = await settingsService.ResetSystemPromptAsync();
            if (result is not null)
            {
                SystemPrompt = result.SystemPrompt;
                IsDefault = true;
                StatusMessage = "System prompt reset to default.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error resetting system prompt: {ex.Message}";
        }
    }

    private bool CanSave() => !IsSaving;
}
