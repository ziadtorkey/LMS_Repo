namespace Lms.Domain.Notifications;

public static class NotificationDeepLinks
{
    /// <summary>
    /// Null is fine (no link). Otherwise only an app-relative path: it starts with a single "/" (so no scheme and no
    /// protocol-relative "//host"), and has no backslash or control characters that browsers read as a host separator.
    /// </summary>
    public static bool IsValid(string? deepLink)
    {
        if (deepLink is null)
        {
            return true;
        }

        if (deepLink.Length == 0 || deepLink[0] != '/' || (deepLink.Length > 1 && (deepLink[1] == '/' || deepLink[1] == '\\')))
        {
            return false;
        }

        return deepLink.All(c => c != '\\' && !char.IsControl(c));
    }
}
