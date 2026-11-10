using System.Reflection;

namespace Lms.Shared.Authorization;

/// <summary>
/// Fixed permission catalog. Values follow "Module.Action" (PRD §3.3).
/// Later milestones append nested classes; never rename an existing value.
/// </summary>
public static class Permissions
{
    public static class OrgUnits
    {
        public const string View = "OrgUnits.View";
        public const string Create = "OrgUnits.Create";
        public const string Update = "OrgUnits.Update";
        public const string Delete = "OrgUnits.Delete";
    }

    public static class Roles
    {
        public const string View = "Roles.View";
        public const string Create = "Roles.Create";
        public const string Update = "Roles.Update";
        public const string Delete = "Roles.Delete";
    }

    public static class Assignments
    {
        public const string View = "Assignments.View";
        public const string Create = "Assignments.Create";
        public const string Delete = "Assignments.Delete";
    }

    public static class Settings
    {
        public const string View = "Settings.View";
        public const string Update = "Settings.Update";
    }

    public static class Policies
    {
        public const string View = "Policies.View";
        public const string Update = "Policies.Update";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
        public const string Export = "Audit.Export";
    }

    public static class Files
    {
        public const string Upload = "Files.Upload";
        public const string Download = "Files.Download";
    }

    private static readonly Lazy<IReadOnlyList<string>> AllLazy = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray());

    /// <summary>Every permission in the catalog (reflection, cached).</summary>
    public static IReadOnlyList<string> All => AllLazy.Value;

    /// <summary>
    /// Permissions reserved for the Platform Admin (above all clients). No built-in or custom
    /// client role may hold them. Empty in M0; M1 adds Clients.* here.
    /// </summary>
    public static IReadOnlyList<string> PlatformOnly { get; } = [];
}
