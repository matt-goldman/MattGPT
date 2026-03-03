using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService;

/// <summary>
/// Authentication handler that trusts the <c>X-User-Id</c> header for
/// service-to-service calls within the Aspire network (Blazor Web → API).
/// <para>
/// In the Aspire hosting model, the API service is not directly reachable from
/// the internet — only from other services in the Aspire app. The Blazor Web
/// frontend authenticates the user via its own cookie, then forwards the user
/// ID to the API via this trusted header.
/// </para>
/// </summary>
public class ServiceToServiceAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ServiceToService";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Context.Request.Headers["X-User-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
