using System.Security.Claims;

namespace MattGPT.Web.Auth.NetCoreId;

/// <summary>
/// Delegating handler that attaches authentication context to outgoing HTTP requests from the
/// Blazor Web frontend to the API service for the legacy Identity provider.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor to retrieve the current user's claims.</param>
public class NetCoreIdAuthDelegatingHandler(
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var userId = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is not null)
            {
                request.Headers.TryAddWithoutValidation("X-User-Id", userId);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
