using Duende.IdentityModel.OidcClient;
using Microsoft.Extensions.Logging;

namespace MattGPT.Mobile.Services;

public class KeycloakAuthService(OidcClient oidcClient, ILogger<KeycloakAuthService> logger)
{
    private const string AccessTokenKey = "oidc_access_token";
    private const string RefreshTokenKey = "oidc_refresh_token";
    private const string TokenExpiryKey = "oidc_token_expiry";
    private const string IdTokenKey = "oidc_id_token";

    /// <summary>
    /// Initiates the OIDC Authorization Code + PKCE login flow via the system browser.
    /// Returns true if the user successfully authenticated.
    /// </summary>
    public async Task<bool> LoginAsync()
    {
        try
        {
            var result = await oidcClient.LoginAsync(new LoginRequest());

            if (result.IsError)
            {
                logger.LogError("Login error: {error} due to {description}", result.Error, result.ErrorDescription);
                return false;
            }

            await StoreTokensAsync(
                result.AccessToken,
                result.RefreshToken,
                result.AccessTokenExpiration,
                result.IdentityToken);

            return true;
        }
        catch (Exception ex)
        {
            // Log the exception as needed; for now, just return false to indicate login failure
            logger.LogError(ex, "Exception during login");
            return false;
        }
    }

    /// <summary>
    /// Clears locally stored tokens and attempts end-session with the identity provider.
    /// </summary>
    public async Task LogoutAsync()
    {
        var idToken = await SecureStorage.GetAsync(IdTokenKey);

        if (idToken is not null)
        {
            try
            {
                await oidcClient.LogoutAsync(new LogoutRequest { IdTokenHint = idToken });
            }
            catch
            {
                // Best-effort remote logout; always clear local tokens regardless
            }
        }

        ClearTokens();
    }

    /// <summary>
    /// Returns a valid access token, refreshing if expired. Returns null if no token is available
    /// (i.e. user needs to log in).
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        var accessToken = await SecureStorage.GetAsync(AccessTokenKey);
        var expiryStr = await SecureStorage.GetAsync(TokenExpiryKey);

        if (accessToken is not null
            && expiryStr is not null
            && DateTimeOffset.TryParse(expiryStr, out var expiry)
            && expiry > DateTimeOffset.UtcNow.AddSeconds(30)) // 30s buffer
        {
            return accessToken;
        }

        // Attempt token refresh
        var refreshToken = await SecureStorage.GetAsync(RefreshTokenKey);
        if (refreshToken is not null)
        {
            var result = await oidcClient.RefreshTokenAsync(refreshToken);
            if (!result.IsError)
            {
                await StoreTokensAsync(
                    result.AccessToken,
                    result.RefreshToken,
                    result.AccessTokenExpiration,
                    result.IdentityToken);

                return result.AccessToken;
            }
        }

        // No valid token and refresh failed — caller should trigger LoginAsync
        return null;
    }

    private static async Task StoreTokensAsync(
        string accessToken,
        string? refreshToken,
        DateTimeOffset expiry,
        string? identityToken)
    {
        await SecureStorage.SetAsync(AccessTokenKey, accessToken);
        await SecureStorage.SetAsync(TokenExpiryKey, expiry.ToString("O"));

        if (refreshToken is not null)
            await SecureStorage.SetAsync(RefreshTokenKey, refreshToken);

        if (identityToken is not null)
            await SecureStorage.SetAsync(IdTokenKey, identityToken);
    }

    private static void ClearTokens()
    {
        SecureStorage.Remove(AccessTokenKey);
        SecureStorage.Remove(RefreshTokenKey);
        SecureStorage.Remove(TokenExpiryKey);
        SecureStorage.Remove(IdTokenKey);
    }
}
