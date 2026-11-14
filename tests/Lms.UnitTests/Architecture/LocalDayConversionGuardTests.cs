using System.Text.RegularExpressions;

namespace Lms.UnitTests.Architecture;

/// <summary>
/// M5 L2: only <c>ClientCalendar</c> turns instants into the client's local school days. The M5 modules (grading, timetable, attendance, excuses) must not
/// convert with <c>TimeZoneInfo</c> or <c>DateOnly.FromDateTime(...UtcNow...)</c> themselves; they ask <c>IClientCalendar</c>.
/// </summary>
public sealed class LocalDayConversionGuardTests
{
    private static readonly string[] M5Folders =
    [
        "src/Lms.Infrastructure/Timetable", "src/Lms.Infrastructure/Attendance", "src/Lms.Infrastructure/Excuses",
        "src/Lms.Api/Features/Timetable", "src/Lms.Api/Features/Attendance", "src/Lms.Api/Features/Excuses",
    ];

    [Fact]
    public void M5_modules_do_not_convert_instants_to_local_days_themselves()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var folder in M5Folders.Select(f => Path.Combine(root, f)).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"TimeZoneInfo|DateOnly\.FromDateTime|\.ToLocalTime\(|DateTime\.Now\b"))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_client_calendar_is_the_one_place_that_uses_time_zones()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Infrastructure", "Calendar", "ClientCalendar.cs"));

        Assert.Contains("TimeZoneInfo.ConvertTimeFromUtc", text);
    }

    private static string RepoRoot()
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
