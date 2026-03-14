using MattGPT.ApiClient.Services;

namespace MattGPT.Mobile.Services;

internal class KeycloakAuthFailureHandler(KeycloakAuthService authService) : IAuthFailureHandler
{
    public async Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        // Re-authenticate via the OIDC browser flow; returns true if login succeeds
        // so the caller can retry the failed request.
        return await authService.LoginAsync();
    }
}
