using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MattGPT.Mobile.Services;
using Plugin.Maui.SmartNavigation.Attributes;

namespace MattGPT.Mobile.ViewModels;

[Ignore]
public partial class AuthViewModel(
    IPopupService popupService,
    MobileAuthService authService) : ObservableObject
{
    [ObservableProperty]
    private string _username = string.Empty;
    [ObservableProperty]
    private string _password = string.Empty;
    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isErrorState = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoginMode = true;

    [ObservableProperty]
    private bool _isRegisterMode = false;

    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        IsRegisterMode = !IsRegisterMode;
        ClearFields();
    }

    private void ClearFields()
    {
        Username = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        ErrorMessage = string.Empty;
        IsErrorState = false;
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        IsErrorState = false;
        ErrorMessage = string.Empty;
        bool isLoggedIn = false;

        try
        {
            await authService.LoginAsync(Username, Password);
            isLoggedIn = true;
        }
        catch (Exception ex)
        {
            IsErrorState = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (isLoggedIn)
        {
            await popupService.ClosePopupAsync(Shell.Current);
        }
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
            return;

        if (Password != ConfirmPassword)
        {
            IsErrorState = true;
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        IsErrorState = false;
        ErrorMessage = string.Empty;
        bool isRegistered = false;
        
        try
        {
            await authService.RegisterAsync(Username, Password);
            isRegistered = true;
        }
        catch (Exception ex)
        {
            IsErrorState = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (isRegistered)
        {
            SuccessMessage = "Registration successful! Please log in.";
            ToggleMode();
        }
    }
}