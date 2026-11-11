using System.Reflection;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Every M1 endpoint with the expected status per caller (M1 plan Task 35; Review Focus 1, 4), collected from the
/// per-module files in <c>Security/ProbeRows/</c> (one <see cref="IProbeRowModule"/> per feature). Rows whose route is
/// not mapped yet are reported as pending and become live as soon as the feature merges.
/// Callers: RootAdmin (A root), School1Admin, School2Admin (sibling), School1Teacher, ClientBAdmin, PlatformAdmin (no
/// context), Anonymous. Targets live in client A at or below School1 unless the row says otherwise.
/// </summary>
public static class M1ProbeRows
{
    /// <summary>The module types, ordered by name so the theory data is stable.</summary>
    public static IReadOnlyList<Type> Modules { get; } =
        typeof(M1ProbeRows).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IProbeRowModule).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<ProbeRow> All { get; } =
        Modules.SelectMany(t => ((IProbeRowModule)Activator.CreateInstance(t)!).Rows).ToList();

    /// <summary>Classes in the ProbeRows naming scheme; each must be a module, or its rows would be silently lost.</summary>
    public static IReadOnlyList<Type> NamedLikeModules { get; } =
        typeof(M1ProbeRows).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("ProbeRows", StringComparison.Ordinal) && t != typeof(M1ProbeRows)
                        && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
            .ToList();
}
