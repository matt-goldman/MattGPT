using MattGPT.ApiClient.Services;
using Microsoft.AspNetCore.Components;

namespace MattGPT.Web.Auth.Keycloak;

/// <summary>
/// Handles auth failures for the Keycloak OIDC path by redirecting the user to the login endpoint.
/// </summary>
public sealed class KeycloakAuthFailureHandler(NavigationManager navigation) : IAuthFailureHandler
{
    public Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        navigation.NavigateTo("/auth/login-oidc?returnUrl=" + Uri.EscapeDataString(navigation.Uri), forceLoad: true);
        return Task.FromResult(false);
    }
}
