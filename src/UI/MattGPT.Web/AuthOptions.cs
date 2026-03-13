namespace MattGPT.Web;

/// <summary>
/// Configuration options for optional authentication in the Web frontend.
/// Should mirror the API service's <c>Auth:Enabled</c> and <c>Auth:Provider</c> settings.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When <c>true</c>, the web app shows login UI and requires authentication.
    /// When <c>false</c> (default), the app is open without authentication.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Authentication provider. <c>"Keycloak"</c> (default) uses OIDC against a Keycloak server.
    /// <c>"Identity"</c> uses the legacy cookie + ASP.NET Core Identity API flow.
    /// </summary>
    public string Provider { get; set; } = "Keycloak";
}
