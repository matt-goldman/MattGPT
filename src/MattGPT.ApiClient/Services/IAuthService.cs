using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>
/// API client for authentication operations: logging in, registering,
/// and retrieving user information.
/// </summary>
public interface IAuthService
{
    /// <summary>Authenticates a user and returns a bearer token on success.</summary>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Registers a new user account.</summary>
    Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Retrieves user information using the supplied bearer token.</summary>
    Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);
}
