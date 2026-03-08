using System.Security.Claims;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// Resolves the current user's ID from <see cref="HttpContext"/> when auth is enabled.
/// Returns <c>null</c> when auth is disabled or no user is authenticated.
/// All authentication schemes (Identity bearer/cookie and the trusted
/// <see cref="ServiceToServiceAuthHandler"/>) populate <c>ClaimTypes.NameIdentifier</c>,
/// so a single claims read is sufficient.
/// </summary>
public class CurrentUserService(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> authOptions) : ICurrentUserService
{
    public string? UserId
    {
        get
        {
            if (!authOptions.Value.Enabled)
                return null;

            return httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
