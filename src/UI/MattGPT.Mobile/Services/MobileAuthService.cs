using MattGPT.ApiClient.Models;
using MattGPT.ApiClient.Services;
using MattGPT.UI.Auth;
using Microsoft.Maui.Storage;

namespace MattGPT.UI.Services;

internal class MobileAuthService (IAuthService authService)
{
    private const string StoredLoginKey = "StoredLogin";

    private readonly IAuthService _authService = authService;

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var result = await _authService.LoginAsync(email, password, cancellationToken);

        if (ValidateLoginResult(result))
        {
            var storedLogin = new StoredLogin(result!.Token!.AccessToken!, result!.Token!.RefreshToken!, DateTime.UtcNow.AddSeconds(result.Token.ExpiresIn));
            var serializedLogin = System.Text.Json.JsonSerializer.Serialize(storedLogin);
            await SecureStorage.SetAsync(StoredLoginKey, serializedLogin);
        }

        return result;
    }

    public Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
        => _authService.RegisterAsync(email, password, cancellationToken);

    public Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
        => _authService.GetUserInfoAsync(accessToken, cancellationToken);

    public async Task<string?> GetAccessTokenAsync()
    {
        var storedResult = await SecureStorage.GetAsync(StoredLoginKey);

        if (storedResult != null)
        {
            var storedLogin = System.Text.Json.JsonSerializer.Deserialize<StoredLogin>(storedResult);
            
            if (storedLogin is not null && storedLogin.Expires > DateTime.UtcNow)
            {
                return storedLogin.AccessToken;
            }

            var refreshResult = await _authService.RefreshTokenAsync(storedLogin?.RefreshToken ?? string.Empty);

            if (ValidateLoginResult(refreshResult))
            {
                var newStoredLogin = new StoredLogin(refreshResult!.Token!.AccessToken!, refreshResult!.Token!.RefreshToken!, DateTime.UtcNow.AddSeconds(refreshResult.Token.ExpiresIn));
                var serializedLogin = System.Text.Json.JsonSerializer.Serialize(newStoredLogin);
                await SecureStorage.SetAsync(StoredLoginKey, serializedLogin);
                return refreshResult.Token.AccessToken;
            }
        }

        return null;
    }

    private static bool ValidateLoginResult(LoginResult result)
    {
        return result is not null && result.Success && result.Token is not null;
    }

}

internal record StoredLogin(string AccessToken, string RefreshToken, DateTime Expires);
