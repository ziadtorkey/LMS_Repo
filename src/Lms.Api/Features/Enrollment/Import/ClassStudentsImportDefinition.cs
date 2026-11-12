using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Imports;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Import;

/// <summary>
/// Placing students into classes from an Excel file (FR-ENR-03, X-06): one row is one student in one class of the current
/// academic year at the target unit. Validation applies the rules of placing by hand (class and year open, student active
/// and not placed yet, capacity in Block mode); commit places the students through <see cref="IEnrollmentEngine"/>, class by
/// class in file order, so automatic enrollment and restrictions are exactly those of the engine. All or nothing: the
/// framework's commit transaction rolls everything back when any placement fails.
/// </summary>
public sealed class ClassStudentsImportDefinition(
    LmsDbContext db, IClock clock, IAcademicYearResolver years, ISettingsResolver settings, IEnrollmentEngine engine) : IImportDefinition
{
    private const string StudentCode = nameof(StudentCode), ClassCode = nameof(ClassCode), EffectiveOn = nameof(EffectiveOn);

    public string Kind => ImportKinds.ClassStudents;
    public string Permission => Permissions.Enrollment.Import;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new(StudentCode, "كود الطالب", "Student code", true, AsText: true, ExampleValue: "000123"),
        new(ClassCode, "رمز الفصل", "Class code", true, AsText: true, ExampleValue: "G1-A"),
        new(EffectiveOn, "تاريخ البدء", "Effective date", false, ExampleValue: "2026-09-01"),
    ];

    public async Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var errors = new List<RowError>();
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, StudentCode, Key));
        var year = await years.GetCurrentAsync(context.TargetUnit.Path, ct);
        if (year is null)
        {
            errors.Add(new RowError(0, null, "no_current_year"));
            return errors;
        }

        var (classes, students) = await LoadAsync(context.TargetUnit.Path, year, rows, ct);
        var studentIds = students.Values.Select(s => s.Id).ToList();
        var classIds = classes.Values.Select(c => c.Id).ToList();
        var placedIds = (await db.Set<ClassStudent>()
                .Where(p => p.ActiveYearKey == year.Id && studentIds.Contains(p.StudentId))
                .Select(p => p.StudentId).ToListAsync(ct)).ToHashSet();
        var occupancy = await db.Set<ClassStudent>()
            .Where(p => p.AcademicYearId == year.Id && p.Status == ClassStudent.ActiveStatus && classIds.Contains(p.ClassId))
            .GroupBy(p => p.ClassId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var blockedMode = new Dictionary<Guid, bool>();
        var added = new Dictionary<Guid, int>();

        foreach (var row in rows)
        {
            if (ImportChecks.Date(row, EffectiveOn) is { } badDate)
            {
                errors.Add(badDate);
            }

            students.TryGetValue(Key(row.Get(StudentCode)) ?? string.Empty, out var student);
            classes.TryGetValue(Key(row.Get(ClassCode)) ?? string.Empty, out var cls);
            if (student is null)
            {
                errors.Add(new RowError(row.RowNumber, StudentCode, "unknown_student", Value(row.Get(StudentCode))));
            }
            else if (student.Status != PersonStatus.Active)
            {
                errors.Add(new RowError(row.RowNumber, StudentCode, "not_active"));
            }
            else if (placedIds.Contains(student.Id))
            {
                errors.Add(new RowError(row.RowNumber, StudentCode, "already_placed"));
            }

            if (cls is null)
            {
                errors.Add(new RowError(row.RowNumber, ClassCode, "unknown_class", Value(row.Get(ClassCode))));
                continue;
            }

            if (year.Status == AcademicYearStatus.Closed)
            {
                errors.Add(new RowError(row.RowNumber, ClassCode, "year_closed"));
            }
            else if (cls.Status != Class.ActiveStatus)
            {
                errors.Add(new RowError(row.RowNumber, ClassCode, "class_closed"));
            }
            else if (student is not null && student.Status == PersonStatus.Active && !placedIds.Contains(student.Id) && cls.Capacity is { } capacity)
            {
                // Block mode refuses the rows beyond the capacity; Warn mode accepts them (the wizard shows the overflow).
                if (!blockedMode.TryGetValue(cls.Id, out var block))
                {
                    block = (await settings.ResolveAsync(SettingKeys.ClassesCapacityMode, cls.OrgUnitPath, ct)).Value == "Block";
                    blockedMode[cls.Id] = block;
                }

                var next = added.GetValueOrDefault(cls.Id) + 1;
                if (block && occupancy.GetValueOrDefault(cls.Id) + next > capacity)
                {
                    errors.Add(new RowError(row.RowNumber, ClassCode, "class_full"));
                }
                else
                {
                    added[cls.Id] = next;
                }
            }
        }

        return errors;
    }

    public async Task<int> CommitAsync(ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var year = await years.GetCurrentAsync(context.TargetUnit.Path, ct)
            ?? throw new InvalidOperationException("The target unit has no current academic year any more.");
        var (classes, students) = await LoadAsync(context.TargetUnit.Path, year, rows, ct);
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Class by class (and start date by start date), keeping the file order inside each group.
        var groups = rows
            .Select(r => (Row: r, Class: classes[Key(r.Get(ClassCode))!], Student: students[Key(r.Get(StudentCode))!],
                On: ImportChecks.TryParseDate(r.Get(EffectiveOn), out var on) ? on : today))
            .GroupBy(x => (x.Class.Id, x.On));
        foreach (var group in groups)
        {
            // Warn-mode overflow was shown in the preview and is accepted; Block mode was refused at validation.
            await engine.PlaceManyAsync(group.Select(x => x.Student).ToList(), group.First().Class, group.Key.On, true, ct);
        }

        return rows.Count;
    }

    private async Task<(Dictionary<string, Class> Classes, Dictionary<string, StudentProfile> Students)> LoadAsync(
        string targetPath, AcademicYear year, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var classCodes = rows.Select(r => Key(r.Get(ClassCode))).OfType<string>().Distinct().ToList();
        var studentCodes = rows.Select(r => r.Get(StudentCode)?.Trim()).OfType<string>().Where(c => c.Length > 0).Distinct().ToList();

        // Only what is in the target subtree (and the caller's scope) exists for the import.
        var classes = (await db.Set<Class>()
                .Where(c => c.AcademicYearId == year.Id && c.OrgUnitPath.StartsWith(targetPath) && classCodes.Contains(c.CodeKey!))
                .ToListAsync(ct))
            .ToDictionary(c => c.CodeKey!, StringComparer.Ordinal);
        var students = (await db.Set<StudentProfile>()
                .Where(s => s.OrgUnitPath.StartsWith(targetPath) && studentCodes.Contains(s.StudentCode))
                .ToListAsync(ct))
            .ToDictionary(s => Key(s.StudentCode)!, StringComparer.Ordinal);
        return (classes, students);
    }

    private static string? Key(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static Dictionary<string, string> Value(string? value) => new() { ["value"] = value ?? string.Empty };
}
