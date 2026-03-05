using MattGPT.ApiClient.Services;

namespace MattGPT.UI.Auth;

internal partial class AuthDelegatingHandler : DelegatingHandler
{
    private readonly IAuthService _authService;
    public AuthDelegatingHandler(IAuthService authService)
    {
        _authService = authService;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
