using MattGPT.ApiClient.Services;
using Microsoft.AspNetCore.Components;

namespace MattGPT.Web.Auth.NetCoreId;

/// <summary>
/// Handles auth failures for the legacy Identity path by redirecting the user to the login page.
/// </summary>
public sealed class NetCoreIdAuthFailureHandler(NavigationManager navigation) : IAuthFailureHandler
{
    public Task<bool> HandleAsync(CancellationToken cancellationToken = default)
    {
        navigation.NavigateTo("/login", forceLoad: true);
        return Task.FromResult(false);
    }
}
