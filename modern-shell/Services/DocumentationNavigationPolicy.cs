namespace AutoHotkeyUX.Modern.Services;

/// <summary>Defines the browser's offline scope independently of UI event handling.</summary>
internal static class DocumentationNavigationPolicy
{
    internal const string HostName = "ahk-docs.invalid";

    /// <summary>Allows only manual URLs served by the one application-owned virtual host.</summary>
    internal static bool IsOffline(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.AbsolutePath.StartsWith("/docs/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Allows HTTPS references to be offered for explicit in-panel navigation.</summary>
    internal static bool IsExternalReference(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0 && !uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase);
}
