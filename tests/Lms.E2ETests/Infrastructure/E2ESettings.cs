using System.Text.Json;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Where the app runs and which seeded users to sign in with. Credentials are never copied into the tests:
/// they come from the API's dev config (src/Lms.Api/appsettings.Development.json, section "Seed"), and
/// environment variables override them (CI or a non-default setup).
/// </summary>
public sealed record E2ESettings(
    Uri BaseUrl,
    string PlatformAdminEmail,
    string Password,
    string MultiContextEmail,
    bool Headless,
    float SlowMoMs)
{
    public const string SeedDomain = "altagamo3.test";

    public static E2ESettings Load()
    {
        var seed = ReadSeedSection();

        string Pick(string env, string seedKey, string? fallback = null) =>
            Environment.GetEnvironmentVariable(env) is { Length: > 0 } fromEnv
                ? fromEnv
                : seed.TryGetValue(seedKey, out var fromSeed) && fromSeed.Length > 0
                    ? fromSeed
                    : fallback ?? throw new InvalidOperationException(
                        $"E2E setting missing: set {env} or Seed:{seedKey} in src/Lms.Api/appsettings.Development.json.");

        return new E2ESettings(
            BaseUrl: new Uri(Environment.GetEnvironmentVariable("LMS_E2E_BASEURL") ?? "http://localhost:5132"),
            PlatformAdminEmail: Pick("LMS_E2E_ADMIN_EMAIL", "PlatformAdminEmail"),
            Password: Pick("LMS_E2E_PASSWORD", "DefaultPassword"),
            MultiContextEmail: Pick("LMS_E2E_MULTI_EMAIL", "MultiContextEmail", $"multi@{SeedDomain}"),
            Headless: Environment.GetEnvironmentVariable("LMS_E2E_HEADED") is not ("1" or "true"),
            SlowMoMs: float.TryParse(Environment.GetEnvironmentVariable("LMS_E2E_SLOWMO"), out var slow) ? slow : 0);
    }

    /// <summary>A seeded user of the school client by role key, e.g. "teacher" → teacher@altagamo3.test.</summary>
    public static string SeedUser(string localPart) => $"{localPart}@{SeedDomain}";

    private static Dictionary<string, string> ReadSeedSection()
    {
        var path = Path.Combine(RepoPaths.Root, "src", "Lms.Api", "appsettings.Development.json");
        if (!File.Exists(path))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("Seed", out var seed) || seed.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return seed.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }
}

/// <summary>Locates the repository root from the test binaries (the folder holding LMS.slnx).</summary>
public static class RepoPaths
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (LMS.slnx) not found above " + AppContext.BaseDirectory);
    }
}
