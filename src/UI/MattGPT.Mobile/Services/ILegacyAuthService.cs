using MattGPT.ApiClient.Models;

namespace MattGPT.Mobile.Services;

/// <summary>
/// Auth service for the legacy .NET Core Identity flow, where login is handled via in-app forms.
/// </summary>
public interface ILegacyAuthService : IMobileAuthService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default);
}
