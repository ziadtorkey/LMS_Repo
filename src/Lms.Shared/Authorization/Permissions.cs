using System.Collections.Immutable;
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
        public const string Move = "OrgUnits.Move";
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
        public const string Update = "Assignments.Update";
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

    /// <summary>Platform Admin only (see <see cref="PlatformOnly"/>).</summary>
    public static class Clients
    {
        public const string View = "Clients.View";
        public const string Create = "Clients.Create";
        public const string Update = "Clients.Update";
    }

    public static class LevelTypes
    {
        public const string View = "LevelTypes.View";
        public const string Create = "LevelTypes.Create";
        public const string Update = "LevelTypes.Update";
        public const string Delete = "LevelTypes.Delete";
    }

    public static class Templates
    {
        public const string View = "Templates.View";
        public const string Create = "Templates.Create";
        public const string Delete = "Templates.Delete";
        public const string Apply = "Templates.Apply";
    }

    public static class AcademicYears
    {
        public const string View = "AcademicYears.View";
        public const string Create = "AcademicYears.Create";
        public const string Update = "AcademicYears.Update";
        public const string Delete = "AcademicYears.Delete";
    }

    // Immutable list (not a FrozenSet) so existing consumers typed IReadOnlyList<string> keep compiling;
    // ImmutableArray rejects every mutation through IList<string>/ICollection<string>.
    private static readonly Lazy<IReadOnlyList<string>> AllLazy = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToImmutableArray());

    /// <summary>Every permission in the catalog (reflection, cached, immutable).</summary>
    public static IReadOnlyList<string> All => AllLazy.Value;

    /// <summary>
    /// Permissions reserved for the Platform Admin (above all clients). No built-in or custom
    /// client role may hold them (enforced by contract tests and by the roles API).
    /// </summary>
    public static IReadOnlyList<string> PlatformOnly { get; } =
        ImmutableArray.Create(Clients.View, Clients.Create, Clients.Update);
}
