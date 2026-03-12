using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MattGPT.Mobile.Services;
using Plugin.Maui.SmartNavigation.Attributes;
using System.ComponentModel.DataAnnotations;

namespace MattGPT.Mobile.ViewModels;

[Ignore]
public partial class AuthViewModel(
    IPopupService popupService,
    MobileAuthService authService) : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; } = false;

    [ObservableProperty]
    public partial bool IsErrorState { get; set; } = false;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoginMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsRegisterMode { get; set; }

    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        IsRegisterMode = !IsRegisterMode;
        ClearFields();
    }

    private void ClearFields()
    {
        Email = string.Empty;
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

        ValidateAllProperties();
        if (HasErrors)
        {
            IsErrorState = true;
            ErrorMessage = GetErrors(nameof(Email)).FirstOrDefault()?.ErrorMessage
                ?? "Please enter a valid email address.";
            return;
        }

        IsBusy = true;
        IsErrorState = false;
        ErrorMessage = string.Empty;
        bool isLoggedIn = false;

        try
        {
            var result = await authService.LoginAsync(Email, Password);
            if (result.Success)
            {
                isLoggedIn = true;
            }
            else
            {
                IsErrorState = true;
                ErrorMessage = result.ErrorMessage?? "Unable to log you in, please check your credentials and try again (or register).";
            }
        }
        catch (Exception)
        {
            IsErrorState = true;
            ErrorMessage = "An unexpected error occurred. Please try again.";
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

        ValidateAllProperties();
        if (HasErrors)
        {
            IsErrorState = true;
            ErrorMessage = GetErrors(nameof(Email)).FirstOrDefault()?.ErrorMessage
                ?? "Please enter a valid email address.";
            return;
        }

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
            var result = await authService.RegisterAsync(Email, Password);
            if (result.Success)
            {
                isRegistered = true;
            }
            else
            {
                IsErrorState = true;
                ErrorMessage = "Registration failed. Please verify your details and try again.";
            }
        }
        catch (Exception)
        {
            IsErrorState = true;
            ErrorMessage = "An unexpected error occurred. Please try again.";
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