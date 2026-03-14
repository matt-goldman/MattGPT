// See: https://github.com/DuendeSoftware/foss/blob/main/identity-model-oidc-client/samples/Maui/MauiApp1/MauiApp1/MauiAuthenticationBrowser.cs
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient.Browser;

namespace MattGPT.Mobile.Auth;

internal class MobileAuthBrowser : Duende.IdentityModel.OidcClient.Browser.IBrowser
{
    public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new Uri(options.StartUrl), new Uri(options.EndUrl), cancellationToken: cancellationToken);

            var url = new RequestUrl("mattgpt://callback")
                .Create(new Parameters(result.Properties));

            return new BrowserResult
            {
                Response = url,
                ResultType = BrowserResultType.Success,
            };
        }
        catch (TaskCanceledException)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UserCancel
            };
        }
    }
}