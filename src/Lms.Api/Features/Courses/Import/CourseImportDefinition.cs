using System.Text.RegularExpressions;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses.Import;

/// <summary>Excel import of courses (M3 Task 6, X-06): create-only, one owner unit per row, all or nothing.</summary>
public sealed partial class CourseImportDefinition(LmsDbContext db, IAccessEvaluator access) : IImportDefinition
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguagePattern();

    public string Kind => ImportKinds.Courses;
    public string Permission => Permissions.Courses.Import;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new("code", "الرمز", "Code", true, AsText: true, ExampleValue: "MATH-101"),
        new("nameAr", "الاسم بالعربية", "Name (Arabic)", true, ExampleValue: "رياضيات"),
        new("nameEn", "الاسم بالإنجليزية", "Name (English)", true, ExampleValue: "Mathematics"),
        new("descriptionAr", "الوصف بالعربية", "Description (Arabic)", false),
        new("descriptionEn", "الوصف بالإنجليزية", "Description (English)", false),
        new("language", "اللغة", "Language", true, ExampleValue: "en"),
        new("weeklyPeriods", "الحصص الأسبوعية", "Weekly periods", true, ExampleValue: "4"),
        new("ownerUnitCode", "رمز وحدة الملكية", "Owner unit code", true, AsText: true, ExampleValue: "SCH-1"),
        new("allocateToUnitCodes", "تخصيص لرموز الوحدات", "Allocate to unit codes", false, AsText: true, ExampleValue: "SCH-1;SCH-2"),
    ];

    public async Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken)
    {
        var errors = new List<RowError>(ImportChecks.DuplicatesInFile(rows, "code", v => v?.Trim().ToUpperInvariant()));
        foreach (var row in rows)
        {
            if (row.Get("code") is { } code && !CodePattern().IsMatch(code.Trim()))
            {
                errors.Add(new RowError(row.RowNumber, "code", "invalid_code"));
            }

            if (row.Get("language") is { } language && !LanguagePattern().IsMatch(language))
            {
                errors.Add(new RowError(row.RowNumber, "language", "invalid_language"));
            }

            if (row.Get("weeklyPeriods") is { } periods
                && !(int.TryParse(periods, out var n) && n is >= 0 and <= CourseValidators.MaxWeeklyPeriods))
            {
                errors.Add(new RowError(row.RowNumber, "weeklyPeriods", "invalid_weekly_periods"));
            }

            if (await OwnerErrorAsync(row, context, cancellationToken) is { } ownerError)
            {
                errors.Add(ownerError);
            }

            errors.AddRange(await AllocationErrorsAsync(row, context, cancellationToken));
        }

        errors.AddRange(await TakenCodesAsync(context, rows, cancellationToken));
        return errors;
    }

    public async Task<int> CommitAsync(
        ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            var owner = context.UnitsByCode[row.Get("ownerUnitCode")!.ToUpperInvariant()];
            var course = new Course(
                owner, CourseValidators.NormalizeCode(row.Get("code")!), row.Get("nameAr")!, row.Get("nameEn")!,
                row.Get("descriptionAr"), row.Get("descriptionEn"), row.Get("language")!, int.Parse(row.Get("weeklyPeriods")!));
            db.Set<Course>().Add(course);
            foreach (var unit in AllocationUnits(row, context.UnitsByCode))
            {
                db.Set<CourseAllocation>().Add(new CourseAllocation(course, unit));
            }
        }

        // A code taken meanwhile violates the live-key unique index: the exception rolls the whole import back.
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private static IEnumerable<string> SplitCodes(ImportRowValues row) =>
        (row.Get("allocateToUnitCodes") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant()).Distinct();

    private static IEnumerable<Lms.Domain.Organizations.OrgUnit> AllocationUnits(
        ImportRowValues row, IReadOnlyDictionary<string, Lms.Domain.Organizations.OrgUnit> units) =>
        SplitCodes(row).Select(c => units[c]);

    /// <summary>Each unit must exist below the target, allow the allocation and be at or below the course owner.</summary>
    private async Task<IEnumerable<RowError>> AllocationErrorsAsync(ImportRowValues row, ImportValidationContext context, CancellationToken ct)
    {
        var errors = new List<RowError>();
        context.UnitsByCode.TryGetValue((row.Get("ownerUnitCode") ?? string.Empty).ToUpperInvariant(), out var owner);
        foreach (var code in SplitCodes(row))
        {
            if (!context.UnitsByCode.TryGetValue(code, out var unit)
                || !(await access.EvaluateAsync(Permissions.Courses.Allocate, unit.Path, ct)).Allowed)
            {
                errors.Add(new RowError(row.RowNumber, "allocateToUnitCodes", "unknown_unit", new Dictionary<string, string> { ["value"] = code }));
            }
            else if (owner is not null && !Lms.Domain.Organizations.OrgUnitPath.AncestorsAndSelf(unit.Path).Contains(owner.Path))
            {
                errors.Add(new RowError(row.RowNumber, "allocateToUnitCodes", "course_not_available"));
            }
        }

        return errors;
    }

    private async Task<RowError?> OwnerErrorAsync(ImportRowValues row, ImportValidationContext context, CancellationToken ct)
    {
        if (row.Get("ownerUnitCode") is not { } unitCode)
        {
            return null;
        }

        if (!context.UnitsByCode.TryGetValue(unitCode.ToUpperInvariant(), out var owner))
        {
            return new RowError(row.RowNumber, "ownerUnitCode", "unknown_unit", new Dictionary<string, string> { ["value"] = unitCode });
        }

        if (!CourseHandlers.CanOwnCourses(owner))
        {
            return new RowError(row.RowNumber, "ownerUnitCode", "invalid_owner_unit");
        }

        var decision = await access.EvaluateAsync(Permissions.Courses.Create, owner.Path, ct);
        if (decision.Allowed)
        {
            return null;
        }

        return decision.Reason == AccessDenialReason.BlockedByPolicy
            ? new RowError(row.RowNumber, "ownerUnitCode", "blocked_by_policy")
            : new RowError(row.RowNumber, "ownerUnitCode", "unknown_unit", new Dictionary<string, string> { ["value"] = unitCode });
    }

    private async Task<IEnumerable<RowError>> TakenCodesAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken ct)
    {
        var keys = rows.Select(r => r.Get("code")).Where(c => c is not null).Select(c => Course.KeyOf(c!)).Distinct().ToList();
        var organizationId = context.TargetUnit.OrganizationId;

        // ISOLATION-BYPASS: course codes are unique per client, so a code used by a sibling unit (outside the caller
        // subtree) must be found. Only live courses of the caller client are read, and only their code keys.
        var taken = (await db.Set<Course>().IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && keys.Contains(c.CodeKey!))
                .Select(c => c.CodeKey!).ToListAsync(ct))
            .ToHashSet();

        return rows
            .Where(r => r.Get("code") is { } code && taken.Contains(Course.KeyOf(code)))
            .Select(r => new RowError(r.RowNumber, "code", "course_code_taken"));
    }
}
