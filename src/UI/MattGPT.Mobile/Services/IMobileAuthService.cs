namespace MattGPT.Mobile.Services;

/// <summary>
/// Common abstraction for mobile authentication services.
/// Implementations cover the Keycloak OIDC flow and the legacy .NET Core Identity flow.
/// </summary>
public interface IMobileAuthService
{
    /// <summary>
    /// Returns a valid access token for the current session, or <see langword="null"/> if the user
    /// is not authenticated (i.e. login is required).
    /// </summary>
    Task<string?> GetAccessTokenAsync();
}
