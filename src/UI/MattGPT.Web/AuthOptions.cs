namespace MattGPT.Web;

/// <summary>
/// Configuration options for optional authentication in the Web frontend.
/// Should mirror the API service's <c>Auth:Enabled</c> setting.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When <c>true</c>, the web app shows login/register UI and requires authentication.
    /// When <c>false</c> (default), the app is open without authentication.
    /// </summary>
    public bool Enabled { get; set; }
}
