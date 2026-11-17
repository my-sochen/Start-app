namespace StartApp.Core;

/// <summary>Privacy boundary for optional icons, NOT a duplicate URL-comparison policy.</summary>
public static class ItemIconPolicy
{
    /// <summary>Request only the site's conventional favicon on that exact origin, never a full saved page, query, fragment or a third-party service.
    /// Redirects are not followed by the caller. Unavailable or nonstandard icons use the generic fallback.</summary>
    public static string? WebIconAddress(string target)
    {
        if (PreflightRules.LinkFormProblem(target) is not null || !Uri.TryCreate(target, UriKind.Absolute, out var uri)) return null;
        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) + "/favicon.ico";
    }
}
