namespace MattGPT.ApiService;

/// <summary>
/// Configuration options for optional authentication.
/// Bound from the <c>Auth</c> section of appsettings.json.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When <c>true</c>, all API endpoints require authentication and data is scoped to the authenticated user.
    /// When <c>false</c> (default), the app runs without authentication and only surfaces untagged (no userId) data.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Authentication provider. <c>"Keycloak"</c> (default) uses an external Keycloak OIDC provider provisioned via
    /// Aspire. <c>"Identity"</c> uses the legacy ASP.NET Core Identity with local database storage.
    /// </summary>
    public string Provider { get; set; } = "Keycloak";

    /// <summary>
    /// When <c>Auth:Provider = "Identity"</c> and <c>true</c> (default), the Identity backing store uses the same
    /// database as <c>DocumentDb:Provider</c> when that provider supports it; otherwise falls back to SQLite.
    /// </summary>
    public bool UseDocumentDbForAuth { get; set; } = true;

    /// <summary>
    /// Explicit auth backing-store provider when <c>UseDocumentDbForAuth = false</c>.
    /// Supported values: <c>"SQLite"</c> (default), <c>"Postgres"</c>.
    /// Only applies when <c>Provider = "Identity"</c>.
    /// </summary>
    public string AuthDbProvider { get; set; } = "SQLite";
}
