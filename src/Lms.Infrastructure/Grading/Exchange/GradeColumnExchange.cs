using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Exchange;

/// <summary>
/// Export, preview and apply of one item's grade column (M5 Task 10, FR-GRD-04, L9). Only a <b>manual</b> item can be imported: an event-derived score is
/// never written (400). The preview validates every row (unknown student number, duplicate, out of range, not a number, excused) and issues a bound token;
/// the apply re-validates against the data as it is now (an item resized, a student who left, a gradebook locked since) and writes through the gradebook
/// rules. Export, preview and apply are audited as security events with counts and ids only, never a value.
/// </summary>
public sealed class GradeColumnExchange(
    LmsDbContext db, IGradebookWriter writer, ImportPreviewToken tokens, IAuditWriter audit, IClock clock)
{
    public async Task<byte[]> ExportAsync(ClassCourse classCourse, GradeItem item, Guid? byUserId, CancellationToken ct)
    {
        var students = await ActiveStudentsAsync(classCourse, ct);
        var ids = students.Select(s => s.Id).ToList();
        var points = await PointsAsync(classCourse, item, ids, ct);
        var rows = students.OrderBy(s => s.StudentCode, StringComparer.Ordinal)
            .Select(s => new ColumnExportRow(s.StudentCode, s.NameEn, s.NameAr, points.GetValueOrDefault(s.Id))).ToList();
        await audit.WriteSecurityEventAsync("Grades.ColumnExported", byUserId, $"gradebook={item.GradebookId}; item={item.Id}; rows={rows.Count}", cancellationToken: ct);
        return GradeColumnExporter.Export(item.TitleEn, item.MaxPoints, rows);
    }

    public async Task<GradeImportPreviewDto> PreviewAsync(
        ClassCourse classCourse, GradeItem item, byte[] file, long maxBytes, Guid byUserId, CancellationToken ct)
    {
        if (!item.IsManual)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400); // an event score is never written by an import
        }

        var parsed = GradeColumnImporter.Read(new MemoryStream(file), maxBytes, ct);
        if (parsed.FileError is not null)
        {
            throw new GradebookRuleException(ProblemCodes.grade_import_invalid, 400);
        }

        var students = await ActiveStudentsAsync(classCourse, ct);
        var byCode = students.GroupBy(s => s.StudentCode, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var current = await PointsAsync(classCourse, item, students.Select(s => s.Id).ToList(), ct);
        var versions = (await db.Set<GradeEntry>().Where(e => e.GradeItemId == item.Id).Select(e => new { e.StudentProfileId, e.Version }).ToListAsync(ct))
            .ToDictionary(e => e.StudentProfileId, e => e.Version);
        var excused = await ExcusedAsync(item, ct);
        var seen = new HashSet<Guid>();
        var rows = new List<GradeImportRowDto>();
        var tokenRows = new List<TokenRow>();
        var unknown = new List<string>();
        foreach (var row in parsed.Rows)
        {
            var (error, student) = Check(row, item, byCode, excused, seen);
            if (error == "unknown_student" && row.StudentNumber.Length <= GradeColumnImporter.MaxStudentNumberLength)
            {
                unknown.Add(row.StudentNumber);
            }

            var overwrites = student is not null && error is null && row.Points is not null && current.ContainsKey(student.Id);
            rows.Add(new GradeImportRowDto(row.Row, row.StudentNumber, row.Points, overwrites, error));
            if (error is null && row.Points is { } points && student is not null)
            {
                tokenRows.Add(new TokenRow(student.Id, points, versions.TryGetValue(student.Id, out var version) ? version : null));
            }
        }

        var (token, expires) = tokens.Issue(new ImportTokenPayload(byUserId, item.GradebookId, item.Id, ImportPreviewToken.Sha256(file), tokenRows));
        await audit.WriteSecurityEventAsync(
            "Grades.ImportPreviewed", byUserId, $"gradebook={item.GradebookId}; item={item.Id}; file={ImportPreviewToken.Sha256(file)}; rows={rows.Count}; accepted={tokenRows.Count}", cancellationToken: ct);
        return new GradeImportPreviewDto(
            token, expires, rows.Count, rows.Count(r => r.Overwrites), unknown.Distinct().ToList(),
            rows.Where(r => r.ErrorCode == ProblemCodes.grade_out_of_range).ToList(),
            rows.Where(r => r.ErrorCode is not null && r.ErrorCode != ProblemCodes.grade_out_of_range && r.ErrorCode != "unknown_student").ToList(), rows);
    }

    public async Task<GradeImportResultDto> ApplyAsync(ClassCourse classCourse, GradeItem item, string token, Guid byUserId, CancellationToken ct)
    {
        var payload = tokens.Read(token);
        if (payload is null || payload.UploaderUserId != byUserId || payload.GradebookId != item.GradebookId || payload.ItemId != item.Id)
        {
            throw new GradebookRuleException(ProblemCodes.grade_import_expired, 400); // expired, forged, or made for another user, gradebook or item
        }

        if (!item.IsManual)
        {
            throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        }

        var gradebook = await db.Set<Gradebook>().SingleAsync(g => g.Id == item.GradebookId, ct);
        await writer.EnsureWritableAsync(gradebook, ct); // a gradebook locked since the preview refuses the apply (409)
        var enrolled = (await ActiveStudentsAsync(classCourse, ct)).Select(s => s.Id).ToHashSet();
        var excused = await ExcusedAsync(item, ct);
        var skipped = new List<GradeImportRowDto>();
        var written = 0;
        var now = clock.UtcNow;
        foreach (var row in payload.Rows)
        {
            string? error = null;
            if (!enrolled.Contains(row.StudentId))
            {
                error = "unknown_student"; // left the course since the preview
            }
            else if (row.Points < 0 || row.Points > item.MaxPoints)
            {
                error = ProblemCodes.grade_out_of_range; // the item was resized since the preview
            }
            else if (excused.Contains(row.StudentId))
            {
                error = "excused";
            }

            if (error is not null)
            {
                skipped.Add(new GradeImportRowDto(0, row.StudentId.ToString("N"), row.Points, false, error));
                continue;
            }

            var entry = await db.Set<GradeEntry>().FirstOrDefaultAsync(e => e.GradeItemId == item.Id && e.StudentProfileId == row.StudentId, ct);
            if ((entry?.Version) != row.EntryVersion)
            {
                // someone (or an earlier apply of this same token) changed this cell since the preview: never overwritten silently (L12)
                skipped.Add(new GradeImportRowDto(0, row.StudentId.ToString("N"), row.Points, false, "changed_since_preview"));
                continue;
            }

            if (entry is null)
            {
                entry = new GradeEntry(item, row.StudentId, byUserId, now);
                db.Add(entry);
            }

            entry.SetPoints(row.Points, byUserId, now);
            written++;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteSecurityEventAsync(
            "Grades.ImportApplied", byUserId, $"gradebook={item.GradebookId}; item={item.Id}; file={payload.FileSha256}; written={written}; skipped={skipped.Count}", cancellationToken: ct);
        return new GradeImportResultDto(written, skipped);
    }

    private static (string? Error, StudentProfile? Student) Check(
        ParsedGradeRow row, GradeItem item, IReadOnlyDictionary<string, StudentProfile> byCode, HashSet<Guid> excused, HashSet<Guid> seen)
    {
        if (row.ErrorCode is not null)
        {
            return (row.ErrorCode, null);
        }

        if (!byCode.TryGetValue(row.StudentNumber, out var student))
        {
            return ("unknown_student", null);
        }

        if (!seen.Add(student.Id))
        {
            return ("duplicate_student", student);
        }

        if (row.Points is { } points && (points < 0 || points > item.MaxPoints))
        {
            return (ProblemCodes.grade_out_of_range, student);
        }

        return (excused.Contains(student.Id) ? "excused" : null, student);
    }

    private async Task<List<StudentProfile>> ActiveStudentsAsync(ClassCourse classCourse, CancellationToken ct)
    {
        var active = db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus).Select(e => e.StudentId);
        return await db.Set<StudentProfile>().Where(s => active.Contains(s.Id)).ToListAsync(ct);
    }

    private async Task<HashSet<Guid>> ExcusedAsync(GradeItem item, CancellationToken ct) =>
        (await db.Set<GradeEntry>().Where(e => e.GradeItemId == item.Id && e.IsExcused).Select(e => e.StudentProfileId).ToListAsync(ct)).ToHashSet();

    /// <summary>The stored points of an item per student: a manual score, an override, or (event items) the latest event score.</summary>
    private async Task<Dictionary<Guid, decimal>> PointsAsync(ClassCourse classCourse, GradeItem item, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, decimal>();
        if (!item.IsManual)
        {
            var events = await db.Set<GradeEvent>()
                .Where(e => e.ClassCourseId == classCourse.Id && (int)e.SourceKind == item.SourceKind && e.SourceId == item.SourceId && ids.Contains(e.StudentProfileId))
                .ToListAsync(ct);
            foreach (var group in events.GroupBy(e => e.StudentProfileId))
            {
                result[group.Key] = group.OrderByDescending(e => e.Revision).First().Points;
            }
        }

        var entries = await db.Set<GradeEntry>().Where(e => e.GradeItemId == item.Id && !e.IsExcused && e.Points != null).ToListAsync(ct);
        foreach (var entry in entries)
        {
            result[entry.StudentProfileId] = entry.Points!.Value;
        }

        return result;
    }
}
