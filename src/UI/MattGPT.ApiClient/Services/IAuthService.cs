using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <summary>
/// API client for authentication operations: logging in, registering,
/// and retrieving user information.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a user asynchronously using the specified email address and password.
    /// </summary>
    /// <remarks>An exception is thrown if the email or password is invalid, or if the operation is
    /// canceled.</remarks>
    /// <param name="email">The email address of the user attempting to log in. This parameter cannot be null or empty.</param>
    /// <param name="password">The password associated with the specified email address. This parameter cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation. The default value is <see
    /// cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous login operation. The task result contains a <see cref="LoginResult"/>
    /// that indicates whether the authentication was successful.</returns>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously registers a new user account using the specified email address and password.
    /// </summary>
    /// <remarks>The method may throw an exception if the email address is already in use or if the password
    /// does not meet the required criteria.</remarks>
    /// <param name="email">The email address to associate with the new user account. This value must be a valid email format and cannot be
    /// null or empty.</param>
    /// <param name="password">The password for the new user account. The password must meet the application's security requirements, such as
    /// minimum length and complexity.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the registration operation.</param>
    /// <returns>A task that represents the asynchronous registration operation. The task result contains a RegisterResult
    /// indicating whether the registration succeeded or failed.</returns>
    Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves user information associated with the specified access token.
    /// </summary>
    /// <remarks>This method may throw exceptions if the access token is invalid, expired, or if an error
    /// occurs during retrieval. Callers should handle potential exceptions as appropriate.</remarks>
    /// <param name="accessToken">The access token used to authenticate the request. This value cannot be null or empty.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation. The default value is <see
    /// cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="UserInfo"/> object if
    /// the user is found; otherwise, <see langword="null"/>.</returns>
    Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously obtains a new access token using the specified refresh token.
    /// </summary>
    /// <remarks>This method throws an exception if the provided refresh token is invalid or expired. Callers
    /// should handle such exceptions to ensure robust authentication flows.</remarks>
    /// <param name="refreshToken">The refresh token to use for requesting a new access token. This token must be valid and unexpired.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="LoginResult"/> with the
    /// new access token and its expiration information.</returns>
    Task<LoginResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
