using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Domain.Grading.Calculation;

namespace Lms.UnitTests.Grading;

/// <summary>
/// Hand-computed gradebooks (<c>Grading/Golden/*.json</c>, each with its working in <c>handComputation</c>): a Saudi school gradebook with
/// the five percentage bands, a university 5-point GPA gradebook, and a published-only gradebook with the hide-until-complete policy. The
/// expected numbers were worked out by hand, not produced by the engine.
/// </summary>
public sealed class GradebookGoldenFileTests
{
    public static TheoryData<string> Files() => new(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Grading", "Golden"), "*.json").Select(Path.GetFileName).Order().ToArray()!);

    private static Guid Id(string text) => new(MD5.HashData(Encoding.UTF8.GetBytes(text)));

    [Theory]
    [MemberData(nameof(Files))]
    public void The_engine_reproduces_the_hand_computed_gradebook(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Grading", "Golden", file)));
        var root = document.RootElement;
        var input = Read(root);

        var result = GradebookCalculator.Calculate(input);

        Assert.True(result.IsValid, $"{file}: {string.Join("; ", result.Warnings)}");
        foreach (var expected in root.GetProperty("expected").EnumerateArray())
        {
            var student = result.Students.Single(s => s.StudentId == Id(expected.GetProperty("student").GetString()!));
            var final = expected.GetProperty("final");
            Assert.True(
                final.ValueKind == JsonValueKind.Null ? student.FinalPercent is null : student.FinalPercent == final.GetDecimal(),
                $"{file} {expected.GetProperty("student")}: expected {final}, got {student.FinalPercent}");
            var band = expected.GetProperty("band");
            Assert.Equal(band.ValueKind == JsonValueKind.Null ? null : band.GetString(), student.Band?.LabelEn);
            if (expected.TryGetProperty("gpa", out var gpa))
            {
                Assert.Equal(gpa.GetDecimal(), student.Band!.GpaPoints);
            }

            Assert.Equal(expected.GetProperty("complete").GetBoolean(), student.IsComplete);
        }

        Assert.Equal(root.GetProperty("expected").GetArrayLength(), result.Students.Count);
    }

    private static CalculationInput Read(JsonElement root)
    {
        var rules = root.GetProperty("rules");
        var calcRules = new CalcRules(
            Enum.Parse<MissingPolicy>(rules.GetProperty("missing").GetString()!), Enum.Parse<CategoryMethod>(rules.GetProperty("method").GetString()!),
            rules.GetProperty("decimalPlaces").GetInt32(), rules.GetProperty("publishedOnly").GetBoolean());
        var bands = root.GetProperty("bands").EnumerateArray()
            .Select(b => new CalcBand(b.GetProperty("min").GetDecimal(), b.GetProperty("ar").GetString()!, b.GetProperty("en").GetString()!,
                b.TryGetProperty("gpa", out var g) ? g.GetDecimal() : null)).ToList();
        var categories = root.GetProperty("categories").EnumerateArray().Select(c => new CalcCategory(Id(c.GetProperty("id").GetString()!), c.GetProperty("weight").GetDecimal())).ToList();
        var items = root.GetProperty("items").EnumerateArray().Select(i => new CalcItem(
            Id(i.GetProperty("id").GetString()!), Id(i.GetProperty("category").GetString()!), i.GetProperty("max").GetDecimal(),
            !i.TryGetProperty("published", out var p) || p.GetBoolean(), i.TryGetProperty("sourceKind", out var k) ? k.GetInt32() : null)).ToList();
        var events = root.GetProperty("events").EnumerateArray().Select(e => new CalcEvent(
            Id(e.GetProperty("item").GetString()!), Id(e.GetProperty("student").GetString()!), e.GetProperty("revision").GetInt32(), e.GetProperty("points").GetDecimal())).ToList();
        var entries = root.GetProperty("entries").EnumerateArray().Select(e => new CalcEntry(
            Id(e.GetProperty("item").GetString()!), Id(e.GetProperty("student").GetString()!), e.TryGetProperty("points", out var pts) ? pts.GetDecimal() : null,
            e.TryGetProperty("override", out var o) && o.GetBoolean(), e.TryGetProperty("excused", out var x) && x.GetBoolean())).ToList();
        return new CalculationInput(categories, items, events, entries, bands, calcRules);
    }
}
