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
            db.Set<Course>().Add(new Course(
                owner, CourseValidators.NormalizeCode(row.Get("code")!), row.Get("nameAr")!, row.Get("nameEn")!,
                row.Get("descriptionAr"), row.Get("descriptionEn"), row.Get("language")!, int.Parse(row.Get("weeklyPeriods")!)));
        }

        // A code taken meanwhile violates the live-key unique index: the exception rolls the whole import back.
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
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
