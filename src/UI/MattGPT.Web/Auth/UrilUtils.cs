namespace MattGPT.Web.Auth;

public static class UrlUtils
{
    /// <summary>
    /// Determines whether a given URL is a local URL (i.e., it does not point to an external site).
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns>True if the URL is local; otherwise, false.</returns>
    public static bool IsLocalUrl(this string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // Based on Microsoft.AspNetCore.Mvc.IUrlHelper.IsLocalUrl logic:
        if (url[0] == '/')
        {
            // Allow "/" or "/foo", but not "//" or "/\"
            if (url.Length == 1)
            {
                return true;
            }

            return url[1] != '/' && url[1] != '\\';
        }

        // Allow application-relative URLs like "~/foo"
        if (url.Length > 1 && url[0] == '~' && url[1] == '/')
        {
            return true;
        }

        return false;
    }
}