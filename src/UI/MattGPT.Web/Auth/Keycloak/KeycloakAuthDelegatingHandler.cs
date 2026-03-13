using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MattGPT.Web.Auth.Keycloak;

/// <summary>
/// Delegating handler that attaches authentication context to outgoing HTTP requests from the
/// Blazor Web frontend to the API service for the Keycloak Identity provider.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor to retrieve the current user's claims.</param>
public class KeycloakAuthDelegatingHandler(
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            // Forward the OIDC access token as a Bearer header.
            var accessToken = await httpContext.GetTokenAsync("access_token");
            if (accessToken is not null)
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
