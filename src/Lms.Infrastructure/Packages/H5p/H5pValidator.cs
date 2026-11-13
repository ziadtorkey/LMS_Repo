using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Domain.Packages;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.H5p;

/// <summary>
/// The H5P part of package validation (M4b Task 11, host-independent half): <c>h5p.json</c>, <c>content/content.json</c> and every
/// <c>library.json</c> are read through <see cref="SafeJson"/> (1 MB, depth 32, item cap), <c>h5p.json</c> must name a title and a main
/// library that is one of its preloaded dependencies, machine names are plain identifiers (never a path), and every library that is
/// needed, directly or through another library, must be present as a <c>{machineName}-{major}.{minor}</c> folder whose
/// <c>library.json</c> says the same. Anything else is <c>h5p_invalid</c>. The entry rules (no scripts, no nested archives, no unsafe
/// paths) were already applied by the archive reader. Only <c>preloadedDependencies</c> are followed: libraries that are only listed as
/// <c>dynamicDependencies</c> or <c>editorDependencies</c> are neither validated nor required (content may fail at run time, it cannot leave
/// the sandbox); whether to require the dynamic ones is a Task 11 decision.
/// </summary>
public sealed partial class H5pValidator : IPackageValidator
{
    public const string ManifestPath = "h5p.json";

    public const string ContentPath = "content/content.json";

    /// <summary>Most libraries followed (a content with hundreds of libraries is not a content).</summary>
    public const int MaxLibraries = 200;

    public string Kind => PackageKinds.H5p;

    public Task<PackageValidationResult> ValidateAsync(IPackageContent content, CancellationToken ct) =>
        Task.FromResult(Validate(content));

    private static PackageValidationResult Validate(IPackageContent content)
    {
        if (Read(content, ManifestPath) is not { } manifest)
        {
            return Invalid();
        }

        using (manifest)
        {
            var root = manifest.RootElement;
            if (root.ValueKind != JsonValueKind.Object || String(root, "title") is not { Length: > 0 } title
                || String(root, "mainLibrary") is not { } main || !MachineName().IsMatch(main)
                || Dependencies(root) is not { Count: > 0 } dependencies || !dependencies.Any(d => d.Name == main))
            {
                return Invalid();
            }

            if (Read(content, ContentPath) is not { } body)
            {
                return Invalid();
            }

            using (body)
            {
                return body.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array && LibrariesPresent(content, dependencies)
                    ? new PackageValidationResult(null, PackageTitles.Clean(title) ?? main, null, main)
                    : Invalid();
            }
        }
    }

    /// <summary>Follows the preloaded dependencies of the content and of every library found, each needing its folder and a matching library.json.</summary>
    private static bool LibrariesPresent(IPackageContent content, List<Dependency> first)
    {
        // Deduplicated on enqueue: the queue never holds more than MaxLibraries + 1 entries, however many times a library lists the same dependency.
        var queued = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Dependency>();
        foreach (var dependency in first.Where(d => queued.Add(d.Folder)))
        {
            queue.Enqueue(dependency);
        }

        while (queue.Count > 0)
        {
            var dependency = queue.Dequeue();
            if (queued.Count > MaxLibraries || Read(content, dependency.Folder + "/library.json") is not { } library)
            {
                return false;
            }

            using (library)
            {
                var root = library.RootElement;
                if (root.ValueKind != JsonValueKind.Object || String(root, "machineName") != dependency.Name
                    || Number(root, "majorVersion") != dependency.Major || Number(root, "minorVersion") != dependency.Minor)
                {
                    return false;
                }

                if (Dependencies(root, required: false) is not { } next)
                {
                    return false;
                }

                foreach (var follow in next.Where(d => queued.Add(d.Folder)))
                {
                    queue.Enqueue(follow);
                }
            }
        }

        return true;
    }

    /// <summary>The preloaded dependencies of a document: all must be well-formed. <paramref name="required"/> false allows the property to be absent.</summary>
    private static List<Dependency>? Dependencies(JsonElement root, bool required = true)
    {
        if (!root.TryGetProperty("preloadedDependencies", out var array))
        {
            return required ? null : [];
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<Dependency>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || String(item, "machineName") is not { } name || !MachineName().IsMatch(name)
                || Number(item, "majorVersion") is not { } major || Number(item, "minorVersion") is not { } minor)
            {
                return null;
            }

            list.Add(new Dependency(name, major, minor));
        }

        return list;
    }

    private static JsonDocument? Read(IPackageContent content, string path)
    {
        if (!content.Exists(path))
        {
            return null;
        }

        using var stream = content.OpenRead(path);
        return SafeJson.Parse(stream).Document;
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0
            ? number
            : null;

    private static PackageValidationResult Invalid() => PackageValidationResult.Reject(PackageRejectReasons.h5p_invalid);

    private sealed record Dependency(string Name, int? Major, int? Minor)
    {
        public string Folder => $"{Name}-{Major}.{Minor}";
    }

    /// <summary>An H5P machine name: letters, digits, dot, dash and underscore, starting with a letter; never a path.</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{0,99}$")]
    private static partial Regex MachineName();
}
