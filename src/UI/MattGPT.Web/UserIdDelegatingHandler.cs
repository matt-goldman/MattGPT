using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MattGPT.Web;

/// <summary>
/// Delegating handler that attaches authentication context to outgoing HTTP requests from the
/// Blazor Web frontend to the API service.
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Keycloak path</b>: forwards the OIDC access token as a <c>Bearer</c> header so the
///       API service can validate it against Keycloak's JWKS endpoint.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Legacy Identity path</b>: forwards the <c>X-User-Id</c> header with the authenticated
///       user's identifier so the API service can scope data without re-validating the Identity
///       cookie.
///     </description>
///   </item>
/// </list>
/// </summary>
public class UserIdDelegatingHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthOptions> authOptions) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            if (authOptions.Value.Provider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase))
            {
                // Forward the OIDC access token as a Bearer header.
                var accessToken = await httpContext.GetTokenAsync("access_token");
                if (accessToken is not null)
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", accessToken);
                }
            }
            else
            {
                // Legacy Identity: forward the user ID via a trusted internal header.
                var userId = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId is not null)
                {
                    request.Headers.TryAddWithoutValidation("X-User-Id", userId);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
