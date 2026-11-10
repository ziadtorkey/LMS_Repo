namespace Lms.Domain.Organizations;

/// <summary>
/// Materialized path helpers. A path is "/" followed by each ancestor id (Guid "N" format) and a trailing "/",
/// so prefix matching is always segment-safe.
/// </summary>
public static class OrgUnitPath
{
    public static string Append(string parentPath, Guid id) => parentPath + id.ToString("N") + "/";

    public static IReadOnlyList<string> AncestorsAndSelf(string path)
    {
        var result = new List<string>();
        var index = 0;
        while ((index = path.IndexOf('/', index + 1)) >= 0)
        {
            result.Add(path[..(index + 1)]);
        }

        return result;
    }

    public static bool IsWithin(string path, string scopePath) =>
        path.StartsWith(scopePath, StringComparison.Ordinal);
}