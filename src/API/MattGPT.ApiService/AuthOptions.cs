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
}
