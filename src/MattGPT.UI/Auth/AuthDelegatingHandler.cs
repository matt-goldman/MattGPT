using MattGPT.ApiClient.Services;
using MattGPT.UI.Services;

namespace MattGPT.UI.Auth;

internal partial class AuthDelegatingHandler(MobileAuthService authService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await authService.GetAccessTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
