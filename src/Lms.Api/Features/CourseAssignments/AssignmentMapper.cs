using Lms.Api.Features.Content;
using Lms.Domain.Assignments;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>Maps assignment and submission rows to the contract DTOs.</summary>
internal sealed class AssignmentMapper(LmsDbContext db)
{
    public async Task<CourseAssignmentDto> AssignmentAsync(
        Assignment a, LessonModule module, bool isStaff, bool canEdit, DateTime nowUtc, CancellationToken ct)
    {
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        var node = ModuleMapper.Node(module, nowUtc, groups, null);
        var attachments = await (
                from t in db.Set<AssignmentAttachment>()
                join s in db.Set<Asset>() on t.AssetId equals s.Id
                where t.AssignmentId == a.Id
                orderby t.Order
                select s)
            .ToListAsync(ct);
        int? submitted = null, graded = null;
        if (isStaff)
        {
            var statuses = await db.Set<Submission>().Where(s => s.AssignmentId == a.Id && s.Status != Submission.DraftStatus)
                .Select(s => s.Status).ToListAsync(ct);
            submitted = statuses.Count;
            graded = statuses.Count(s => s is SubmissionStatuses.Graded or SubmissionStatuses.Returned);
        }

        return new CourseAssignmentDto(
            a.Id, a.ClassCourseId, module.LessonKey, node, a.InstructionsHtmlAr, a.InstructionsHtmlEn,
            attachments.Select(AssetEndpoints.ToDto).ToList(), ModuleMapper.Utc(a.DueAtUtc), a.MaxGrade, a.LatePolicy, a.PenaltyPercent,
            a.ResubmissionRule, a.AllowText, a.AllowFiles, submitted, graded, canEdit, nowUtc);
    }

    public async Task<SubmissionDto> SubmissionAsync(Submission s, CancellationToken ct)
    {
        var names = await db.Set<Lms.Domain.People.StudentProfile>().Where(p => p.Id == s.StudentId)
            .Select(p => new { p.NameAr, p.NameEn }).FirstAsync(ct);
        var files = await (
                from f in db.Set<SubmissionFile>()
                join a in db.Set<Asset>() on f.AssetId equals a.Id
                where f.SubmissionId == s.Id
                orderby f.CreatedAt
                select a)
            .ToListAsync(ct);
        return new SubmissionDto(
            s.Id, s.AssignmentId, s.StudentId, names.NameAr, names.NameEn, s.Status, s.TextHtml, files.Select(AssetEndpoints.ToDto).ToList(),
            ModuleMapper.Utc(s.SubmittedAtUtc), s.IsLate, s.Number, null);
    }
}
