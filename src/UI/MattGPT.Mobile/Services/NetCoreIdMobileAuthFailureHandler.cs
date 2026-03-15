using MattGPT.ApiClient.Services;

namespace MattGPT.Mobile.Services;

/// <summary>
/// Handles auth failures for the legacy ASP.NET Core Identity path by attempting a silent token refresh.
/// Returns <see langword="true"/> when the token was successfully refreshed and the caller should retry
/// the request; <see langword="false"/> when refresh failed and the user must re-authenticate manually.
/// </summary>
internal class NetCoreIdMobileAuthFailureHandler(NetCoreIdAuthService authService) : IAuthFailureHandler
{
    public async Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        // GetAccessTokenAsync will attempt a silent token refresh if the stored token is expired.
        // Returns null when there is no stored login or the refresh itself failed.
        var token = await authService.GetAccessTokenAsync();
        return token is not null;
    }
}
