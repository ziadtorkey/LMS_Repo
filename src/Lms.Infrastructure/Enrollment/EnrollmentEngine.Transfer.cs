using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Shared;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>Transfer between classes of one academic year (FR-ENR-05, D43).</summary>
public sealed partial class EnrollmentEngine
{
    private sealed record TransferPlan(
        ClassStudent OldPlacement, Class From, OrgUnit ToUnit,
        IReadOnlyList<(CourseEnrollment Old, ClassCourse Target)> Carry,
        IReadOnlyList<(CourseEnrollment Old, ClassCourse OldClassCourse)> Dropped,
        IReadOnlyList<ClassCourse> Added,
        IReadOnlyList<ClassCourse> Restricted,
        IReadOnlyList<CourseEnrollment> AllOld,
        bool OverCapacity);

    /// <summary>What a transfer would do, without changing anything. Throws the same rule exceptions as the transfer itself.</summary>
    public async Task<TransferPreviewDto> PreviewTransferAsync(
        StudentProfile student, Class to, DateOnly on, CancellationToken ct)
    {
        var plan = await PlanTransferAsync(student, to, on, confirmOverCapacity: true, ct);
        var courseIds = plan.Carry.Select(c => c.Target.CourseId)
            .Concat(plan.Dropped.Select(d => d.OldClassCourse.CourseId))
            .Concat(plan.Added.Select(a => a.CourseId))
            .Concat(plan.Restricted.Select(r => r.CourseId)).Distinct().ToList();
        var codes = await guard.CourseCodesAsync(student.OrganizationId, courseIds, ct);
        List<string> Codes(IEnumerable<Guid> ids) => ids.Select(id => codes.GetValueOrDefault(id, string.Empty)).Order().ToList();
        return new TransferPreviewDto(
            Codes(plan.Carry.Select(c => c.Target.CourseId)),
            Codes(plan.Dropped.Select(d => d.OldClassCourse.CourseId)),
            Codes(plan.Added.Select(a => a.CourseId)),
            Codes(plan.Restricted.Select(r => r.CourseId)),
            plan.OverCapacity);
    }

    public Task<StudentTransfer> TransferAsync(
        StudentProfile student, Class to, DateOnly on, string reason, bool confirmOverCapacity, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            // First statement of the transaction (see PlaceManyAsync): capacity of the target is checked against a fresh read.
            await LockClassAsync(to, cancellationToken);
            var plan = await PlanTransferAsync(student, to, on, confirmOverCapacity, cancellationToken);

            // The old placement ends the day before: the one-active-placement key must be free before the new row is inserted.
            var lastDay = on.AddDays(-1);
            await DropAllAsync(plan.AllOld, "transfer", lastDay, cancellationToken);
            plan.OldPlacement.End(ClassStudent.TransferredOutStatus, lastDay, reason);
            await db.SaveChangesAsync(cancellationToken);

            var placement = new ClassStudent(to, student.Id, on);
            db.Set<ClassStudent>().Add(placement);
            await MoveHomesAsync([student], plan.ToUnit, cancellationToken);

            var carried = plan.Carry.Select(c =>
            {
                var enrollment = new CourseEnrollment(c.Target, placement, EnrollmentSources.Transfer, on);
                enrollment.CarryFrom(c.Old.Id); // M5 moves the grades through this link when the setting says so
                return enrollment;
            });
            var added = plan.Added.Select(a => new CourseEnrollment(a, placement, EnrollmentSources.Auto, on));
            AddEnrollments(carried.Concat(added).ToList());

            var transfer = new StudentTransfer(student.Id, plan.From, to, on, reason, current.UserId, clock.UtcNow);
            db.Set<StudentTransfer>().Add(transfer);
            await SaveAsync(ProblemCodes.already_placed, cancellationToken);
            return transfer;
        }, cancellationToken);

    private static EnrollmentRuleException Invalid(string reason) =>
        new(ProblemCodes.transfer_invalid, 400, reason: reason);

    private async Task<TransferPlan> PlanTransferAsync(
        StudentProfile student, Class to, DateOnly on, bool confirmOverCapacity, CancellationToken ct)
    {
        if (student.Status != PersonStatus.Active)
        {
            throw new EnrollmentRuleException(ProblemCodes.validation_failed, 400);
        }

        var year = await guard.EnsureWritableAsync(to, ct);
        var active = await db.Set<ClassStudent>().Where(p => p.StudentId == student.Id && p.Status == ClassStudent.ActiveStatus).ToListAsync(ct);
        var old = active.FirstOrDefault(p => p.AcademicYearId == to.AcademicYearId)
            ?? throw Invalid(active.Count > 0 ? "different_year" : "not_placed");
        if (old.ClassId == to.Id)
        {
            throw Invalid("same_class");
        }

        if (on < year.StartDate || on > year.EndDate)
        {
            throw Invalid("date_outside_year");
        }

        // The old placement ends the day before: it must have had at least one day.
        if (on <= old.From)
        {
            throw Invalid("date_before_placement");
        }

        var from = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == old.ClassId, ct)
            ?? throw new KeyNotFoundException("The class the student comes from is not visible.");
        var fromUnit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == from.OrgUnitId, ct)
            ?? throw new KeyNotFoundException("The unit the student comes from is not visible.");
        var toUnit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == to.OrgUnitId, ct)
            ?? throw new KeyNotFoundException("The target unit is not visible.");
        if (fromUnit.LevelTypeId != toUnit.LevelTypeId)
        {
            throw Invalid("different_level_type"); // D43: across schools only when the level type matches
        }

        var overCapacity = await CheckCapacityAsync(to, 1, confirmOverCapacity, ct);

        var allOld = await db.Set<CourseEnrollment>()
            .Where(e => e.ClassStudentId == old.Id && e.Status == CourseEnrollment.ActiveStatus).ToListAsync(ct);
        var oldCourseIds = allOld.Select(e => e.ClassCourseId).Distinct().ToList();
        var oldCourses = await db.Set<ClassCourse>().Where(c => oldCourseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var targets = await db.Set<ClassCourse>().Where(c => c.ClassId == to.Id).ToListAsync(ct);
        var restrictions = await guard.RestrictionsAsync(
            student.OrganizationId, targets.Select(t => t.CourseId).Concat(oldCourses.Values.Select(c => c.CourseId)).Distinct().ToList(), ct);
        bool Passes(ClassCourse target) => CourseRestrictionEvaluator.Failed(
            restrictions.GetValueOrDefault(target.CourseId) ?? [], student.Gender, student.Religion, toUnit.LevelTypeId).Count == 0;

        var carry = new List<(CourseEnrollment, ClassCourse)>();
        var dropped = new List<(CourseEnrollment, ClassCourse)>();
        var restricted = new List<ClassCourse>();
        foreach (var enrollment in allOld)
        {
            var oldCourse = oldCourses[enrollment.ClassCourseId];
            var target = targets.FirstOrDefault(t => t.CourseId == oldCourse.CourseId);
            if (target is null)
            {
                dropped.Add((enrollment, oldCourse)); // the target class does not offer this course
            }
            else if (!Passes(target) && enrollment.Source != EnrollmentSources.Exceptional)
            {
                dropped.Add((enrollment, oldCourse)); // restrictions are re-checked; an exceptional admission stays
                restricted.Add(target);
            }
            else
            {
                carry.Add((enrollment, target));
            }
        }

        var covered = carry.Select(c => c.Item2.Id).ToHashSet();
        var added = new List<ClassCourse>();
        foreach (var target in targets.Where(t => !t.IsOptional && !covered.Contains(t.Id)))
        {
            if (Passes(target))
            {
                added.Add(target);
            }
            else
            {
                restricted.Add(target);
            }
        }

        return new TransferPlan(old, from, toUnit, carry, dropped, added, restricted, allOld, overCapacity);
    }
}
