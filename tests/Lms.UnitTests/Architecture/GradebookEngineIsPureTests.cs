using Lms.Domain.Grading.Calculation;

namespace Lms.UnitTests.Architecture;

/// <summary>
/// The gradebook engine is a pure function (M5 Task 6): no I/O, no clock, no randomness, no async, no entity. Anything that needs the
/// database, the time or a service belongs to the caller, which hands the engine plain records.
/// </summary>
public sealed class GradebookEngineIsPureTests
{
    private static readonly string[] Forbidden =
    [
        "using System.IO", "using System.Net", "using System.Data", "using System.Diagnostics", "using System.Threading",
        "using Microsoft.EntityFrameworkCore", "using Microsoft.Extensions", "using Lms.Infrastructure", "using Lms.Domain.Abstractions",
        "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset", "TimeProvider", "Random", "File.", "Directory.", "HttpClient", "Task", "async ", "await ",
    ];

    private static string EngineFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Lms.Domain", "Grading", "Calculation");
    }

    [Fact]
    public void The_engine_sources_use_no_io_clock_randomness_or_async()
    {
        var offenders = Directory.GetFiles(EngineFolder(), "*.cs")
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, n) => (file: Path.GetFileName(file), n: n + 1, line))
                .Where(x => !x.line.TrimStart().StartsWith("///") && !x.line.TrimStart().StartsWith("//"))
                .SelectMany(x => Forbidden.Where(f => x.line.Contains(f, StringComparison.Ordinal)).Select(f => $"{x.file}:{x.n} uses '{f}'")))
            .ToList();

        Assert.True(offenders.Count == 0, "The gradebook engine must stay pure:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void No_engine_method_returns_a_task_and_every_public_type_is_a_plain_record_enum_or_static_function()
    {
        var types = typeof(GradebookCalculator).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(GradebookCalculator).Namespace && !t.Name.Contains('<'))
            .ToList();

        Assert.NotEmpty(types);
        Assert.All(types, t =>
        {
            Assert.True(t.IsEnum || (t.IsAbstract && t.IsSealed) || t.GetMethod("<Clone>$") is not null, $"{t.Name} is neither an enum, a static class nor a record");
            Assert.DoesNotContain(t.GetMethods(), m => typeof(Task).IsAssignableFrom(m.ReturnType));
        });
    }
}
