using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;
using Lms.Shared.Grading;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>Builds the staff rows of the grid from the loaded data and the calculation (every cell, hidden items included; the student views are Task 9's).</summary>
public static class GradeRowBuilder
{
    public static IReadOnlyList<GradeRowDto> Build(GradebookData data, CalculationResult result)
    {
        var byStudent = result.Students.ToDictionary(s => s.StudentId);
        var categories = data.Categories.ToDictionary(c => c.Id);
        var entries = data.Entries.ToLookup(e => e.StudentProfileId);
        var events = data.BySource;
        var latest = data.Events
            .Where(e => events.ContainsKey(((int)e.SourceKind, e.SourceId)))
            .GroupBy(e => (Item: events[((int)e.SourceKind, e.SourceId)].Id, Student: e.StudentProfileId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Revision).First());

        return data.Students.Select(student =>
        {
            byStudent.TryGetValue(student.Id, out var calc);
            var own = entries[student.Id].ToDictionary(e => e.GradeItemId);
            var cells = data.Items.Select(item => Cell(item, own.GetValueOrDefault(item.Id), latest.GetValueOrDefault((item.Id, student.Id)))).ToList();
            var categoryResults = (calc?.Categories ?? []).Select(c =>
            {
                var weight = categories.TryGetValue(c.CategoryId, out var cat) ? cat.Weight : 0m;
                return new GradeCategoryResultDto(
                    c.CategoryId, c.Percent, c.Percent is { } p ? Math.Round(p * weight / 100m, data.Gradebook.DecimalPlaces, MidpointRounding.AwayFromZero) : null);
            }).ToList();
            var band = calc?.Band;
            var code = band is null ? null : data.Bands.FirstOrDefault(b => b.Band.MinPercent == band.MinPercent)?.Code;
            return new GradeRowDto(
                student.Id, student.NameAr, student.NameEn, student.Code, cells, categoryResults, calc?.FinalPercent,
                code, band?.LabelAr, band?.LabelEn, band?.GpaPoints, RowVersion(own.Values), student.IsWithdrawn, student.WithdrawnOn);
        }).ToList();
    }

    /// <summary>The version a write sends back: the sum of the row's entry versions (every change of any cell raises it).</summary>
    public static long RowVersion(IEnumerable<GradeEntry> entries) => entries.Sum(e => (long)e.Version);

    private static GradeCellDto Cell(GradeItem item, GradeEntry? entry, GradeEvent? latest)
    {
        if (entry is { IsExcused: true })
        {
            return new GradeCellDto(item.Id, null, GradeCellStates.Excused, null);
        }

        if (entry is { IsOverride: true })
        {
            // recorded after the override (the scoring day itself can be any day)
            var newer = latest is not null && latest.CreatedAt > entry.AtUtc;
            return new GradeCellDto(item.Id, entry.Points, newer ? GradeCellStates.NewerScoreExists : GradeCellStates.Override, entry.Reason);
        }

        if (item.IsManual)
        {
            return entry?.Points is { } p
                ? new GradeCellDto(item.Id, p, GradeCellStates.Manual, null)
                : new GradeCellDto(item.Id, null, GradeCellStates.Empty, null);
        }

        return latest is null
            ? new GradeCellDto(item.Id, null, GradeCellStates.Empty, null)
            : new GradeCellDto(item.Id, latest.Points, GradeCellStates.Event, null);
    }
}
