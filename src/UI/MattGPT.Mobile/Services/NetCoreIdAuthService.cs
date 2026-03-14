using MattGPT.ApiClient.Models;
using MattGPT.ApiClient.Services;
using MattGPT.Mobile.Auth;
using Microsoft.Maui.Storage;

namespace MattGPT.Mobile.Services;

/// <summary>
/// Provides authentication services for mobile applications, including user login, registration, and secure token
/// management.
/// </summary>
/// <remarks>This service securely stores and manages access and refresh tokens using platform-specific secure
/// storage. It acts as a wrapper around the underlying authentication service to facilitate persistent authentication
/// and token refresh scenarios in mobile environments.</remarks>
/// <param name="authService">An instance of the authentication service used to perform user authentication operations.</param>
public class NetCoreIdAuthService (IAuthService authService)
{
    private const string StoredLoginKey = "StoredLogin";

    private readonly IAuthService _authService = authService;

    /// <summary>
    /// Login the user with the provided email and password. If successful, the access token and refresh token will be securely stored for future use.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the login attempt.</returns>
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

    /// <summary>
    /// Asynchronously registers a new user account using the specified email address and password.
    /// </summary>
    /// <remarks>This method communicates with the authentication service to create a new user account. The
    /// email address must not already be associated with an existing account.</remarks>
    /// <param name="email">The email address to associate with the new user account. This value must be a valid email format and cannot be
    /// null or empty.</param>
    /// <param name="password">The password to set for the new user account. The password must meet the application's security requirements.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the registration operation.</param>
    /// <returns>A task that represents the asynchronous registration operation. The task result contains a <see
    /// cref="RegisterResult"/> indicating the outcome of the registration process.</returns>
    public Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
        => _authService.RegisterAsync(email, password, cancellationToken);

    /// <summary>
    /// Asynchronously retrieves user information associated with the specified access token.
    /// </summary>
    /// <remarks>An exception may be thrown if the access token is invalid, expired, or if an error occurs
    /// during the retrieval process.</remarks>
    /// <param name="accessToken">The access token used to authenticate the request and identify the user whose information is to be retrieved.
    /// Cannot be null or empty.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the user information if the access
    /// token is valid; otherwise, null.</returns>
    public Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
        => _authService.GetUserInfoAsync(accessToken, cancellationToken);

    /// <summary>
    /// Asynchronously retrieves a valid access token for the current user, refreshing it if necessary.
    /// </summary>
    /// <remarks>This method checks for a stored login and its expiration. If the stored access token has
    /// expired, it attempts to refresh the token using the stored refresh token. Ensure that SecureStorage is properly
    /// configured to store and retrieve login information before calling this method.</remarks>
    /// <returns>A string containing the access token if available and valid; otherwise, null.</returns>
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
