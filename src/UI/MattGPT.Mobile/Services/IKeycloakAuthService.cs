namespace MattGPT.Mobile.Services;

/// <summary>
/// Auth service for the Keycloak OIDC flow, where login is handled via the system browser.
/// </summary>
public interface IKeycloakAuthService : IMobileAuthService
{
    /// <summary>
    /// Initiates the OIDC Authorization Code + PKCE flow via the system browser.
    /// Returns <see langword="true"/> if the user successfully authenticated.
    /// </summary>
    Task<bool> LoginAsync();
}
