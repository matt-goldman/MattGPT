using System.Security.Claims;

namespace MattGPT.Web;

/// <summary>
/// Delegating handler that adds the <c>X-User-Id</c> header to outgoing HTTP
/// requests when the current Blazor user is authenticated. This is used for
/// service-to-service calls from the Blazor Web frontend to the API service.
/// </summary>
public class UserIdDelegatingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
        {
            request.Headers.Add("X-User-Id", userId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
