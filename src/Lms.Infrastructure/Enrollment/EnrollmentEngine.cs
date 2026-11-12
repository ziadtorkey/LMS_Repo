using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>
/// The only writer of class placements and course enrollments (D9). Every path checks the class, the academic year and
/// the course restrictions. Placement works on whole sets of students (one query per kind of data, not per student and
/// course), saves once, and takes a row lock on the class so concurrent placements cannot both pass the capacity check.
/// </summary>
public sealed partial class EnrollmentEngine(
    LmsDbContext db, PlacementGuard guard, ISettingsResolver settings, IClock clock, ICurrentContext current) : IEnrollmentEngine
{
    public async Task<PlacementResult> PlaceAsync(
        StudentProfile student, Class cls, DateOnly effectiveOn, bool confirmOverCapacity, CancellationToken cancellationToken) =>
        (await PlaceManyAsync([student], cls, effectiveOn, confirmOverCapacity, cancellationToken))[0];

    public Task<IReadOnlyList<PlacementResult>> PlaceManyAsync(
        IReadOnlyList<StudentProfile> students, Class cls, DateOnly effectiveOn, bool confirmOverCapacity, CancellationToken ct) =>
        InTransactionAsync<IReadOnlyList<PlacementResult>>(async () =>
        {
            // First statement of the transaction: the lock must come before any consistent read, or the capacity count
            // below would read a snapshot that predates the placement the previous lock holder committed.
            await LockClassAsync(cls, ct);
            await guard.EnsureWritableAsync(cls, ct);
            if (students.Any(s => s.OrganizationId != cls.OrganizationId))
            {
                throw new EnrollmentRuleException(ProblemCodes.validation_failed, 400);
            }

            if (students.Any(s => s.Status != PersonStatus.Active))
            {
                throw new EnrollmentRuleException(ProblemCodes.validation_failed, 400);
            }

            var ids = students.Select(s => s.Id).ToList();
            if (await db.Set<ClassStudent>().AnyAsync(c => ids.Contains(c.StudentId) && c.ActiveYearKey == cls.AcademicYearId, ct))
            {
                throw new EnrollmentRuleException(ProblemCodes.already_placed);
            }

            var overCapacity = await CheckCapacityAsync(cls, students.Count, confirmOverCapacity, ct);
            var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == cls.OrgUnitId, ct)
                ?? throw new KeyNotFoundException("The class unit is not visible.");

            var placements = students.Select(s => new ClassStudent(cls, s.Id, effectiveOn)).ToList();
            db.Set<ClassStudent>().AddRange(placements);

            await MoveHomesAsync(students, unit, ct);

            var courses = await db.Set<ClassCourse>().Where(c => c.ClassId == cls.Id && !c.IsOptional).ToListAsync(ct);
            var restrictions = await guard.RestrictionsAsync(cls.OrganizationId, courses.Select(c => c.CourseId).Distinct().ToList(), ct);
            var results = BuildEnrollments(students, placements, courses, restrictions, unit, effectiveOn, overCapacity);

            await SaveAsync(ProblemCodes.already_placed, ct);
            return results;
        }, ct);

    public async Task<CourseEnrollment> EnrollManualAsync(
        ClassCourse classCourse, StudentProfile student, string source, CancellationToken cancellationToken)
    {
        if (source is not (EnrollmentSources.Manual or EnrollmentSources.Exceptional))
        {
            throw new ArgumentException("Manual enrollment is for the sources Manual and Exceptional.", nameof(source));
        }

        if (student.Status != PersonStatus.Active)
        {
            throw new EnrollmentRuleException(ProblemCodes.validation_failed, 400);
        }

        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == classCourse.ClassId, cancellationToken)
            ?? throw new KeyNotFoundException("The class is not visible.");
        await guard.EnsureWritableAsync(cls, cancellationToken);

        var placement = await db.Set<ClassStudent>().FirstOrDefaultAsync(
            c => c.ClassId == cls.Id && c.StudentId == student.Id && c.Status == ClassStudent.ActiveStatus, cancellationToken)
            ?? throw new EnrollmentRuleException(ProblemCodes.not_in_class, 400);
        if (!classCourse.IsOptional && source == EnrollmentSources.Manual)
        {
            throw new EnrollmentRuleException(ProblemCodes.enrollment_not_optional);
        }

        if (await db.Set<CourseEnrollment>().AnyAsync(
                e => e.ClassCourseId == classCourse.Id && e.StudentId == student.Id && e.Status == CourseEnrollment.ActiveStatus,
                cancellationToken))
        {
            throw new EnrollmentRuleException(ProblemCodes.already_enrolled);
        }

        if (source != EnrollmentSources.Exceptional)
        {
            var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == cls.OrgUnitId, cancellationToken)
                ?? throw new KeyNotFoundException("The class unit is not visible.");
            var restrictions = await guard.RestrictionsAsync(cls.OrganizationId, [classCourse.CourseId], cancellationToken);
            var failed = CourseRestrictionEvaluator.Failed(
                restrictions.GetValueOrDefault(classCourse.CourseId) ?? [], student.Gender, student.Religion, unit.LevelTypeId);
            if (failed.Count > 0)
            {
                throw new EnrollmentRuleException(ProblemCodes.restriction_not_met, 409, failed);
            }
        }

        var enrollment = new CourseEnrollment(classCourse, placement, source, DateOnly.FromDateTime(clock.UtcNow));
        db.Set<CourseEnrollment>().Add(enrollment);
        await SaveAsync(ProblemCodes.already_enrolled, cancellationToken);
        return enrollment;
    }

    public async Task DropAsync(CourseEnrollment enrollment, string reason, DateOnly on, CancellationToken cancellationToken)
    {
        if (enrollment.Status != CourseEnrollment.ActiveStatus)
        {
            return; // already dropped: nothing to change, the history stays as it was
        }

        var cls = await ClassOfAsync(enrollment.ClassStudentId, cancellationToken);
        await guard.EnsureWritableAsync(cls, cancellationToken);
        await DropAllAsync([enrollment], reason, on, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RemoveFromClassAsync(ClassStudent placement, string reason, DateOnly on, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            if (placement.Status != ClassStudent.ActiveStatus)
            {
                return 0;
            }

            var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == placement.ClassId, cancellationToken)
                ?? throw new KeyNotFoundException("The class is not visible.");
            await guard.EnsureWritableAsync(cls, cancellationToken);

            var active = await db.Set<CourseEnrollment>()
                .Where(e => e.ClassStudentId == placement.Id && e.Status == CourseEnrollment.ActiveStatus)
                .ToListAsync(cancellationToken);
            await DropAllAsync(active, reason, on, cancellationToken);
            placement.End(ClassStudent.RemovedStatus, on, reason);
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }, cancellationToken);

    /// <summary>Drops the enrollments (history stays) and removes their group memberships.</summary>
    internal async Task DropAllAsync(IReadOnlyCollection<CourseEnrollment> enrollments, string reason, DateOnly on, CancellationToken ct)
    {
        if (enrollments.Count == 0)
        {
            return;
        }

        var ids = enrollments.Select(e => e.Id).ToList();
        foreach (var member in await db.Set<GroupMember>().Where(m => ids.Contains(m.EnrollmentId)).ToListAsync(ct))
        {
            db.Set<GroupMember>().Remove(member); // soft delete: frees the one-group-per-class-course key
        }

        foreach (var enrollment in enrollments)
        {
            enrollment.Drop(on, reason);
        }
    }

    private async Task<Class> ClassOfAsync(Guid classStudentId, CancellationToken ct)
    {
        var classId = await db.Set<ClassStudent>().Where(c => c.Id == classStudentId).Select(c => c.ClassId).FirstOrDefaultAsync(ct);
        return await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == classId, ct)
            ?? throw new KeyNotFoundException("The class is not visible.");
    }

    /// <summary>Row lock on the class: concurrent placements into one class run one after the other.</summary>
    private Task LockClassAsync(Class cls, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT `Id` FROM `Classes` WHERE `Id` = {cls.Id} FOR UPDATE", ct);

    /// <returns>True when the class ends up over capacity (Warn mode, confirmed).</returns>
    private async Task<bool> CheckCapacityAsync(Class cls, int adding, bool confirmed, CancellationToken ct)
    {
        if (cls.Capacity is not { } capacity)
        {
            return false;
        }

        var current = await db.Set<ClassStudent>().CountAsync(c => c.ClassId == cls.Id && c.Status == ClassStudent.ActiveStatus, ct);
        if (current + adding <= capacity)
        {
            return false;
        }

        var mode = (await settings.ResolveAsync(SettingKeys.ClassesCapacityMode, cls.OrgUnitPath, ct)).Value;
        if (mode == "Block")
        {
            throw new EnrollmentRuleException(ProblemCodes.class_full);
        }

        if (!confirmed)
        {
            throw new EnrollmentRuleException(ProblemCodes.over_capacity);
        }

        return true;
    }

    /// <summary>Home unit, guardian links and the Student role follow the student into the class unit.</summary>
    private async Task MoveHomesAsync(IReadOnlyList<StudentProfile> students, OrgUnit unit, CancellationToken ct)
    {
        var ids = students.Select(s => s.Id).ToList();
        foreach (var student in students)
        {
            student.MoveHome(unit);
        }

        foreach (var link in await db.Set<StudentGuardian>().Where(l => ids.Contains(l.StudentId)).ToListAsync(ct))
        {
            link.Repoint(students.First(s => s.Id == link.StudentId));
        }

        var userIds = students.Where(s => s.UserId is not null).Select(s => s.UserId!.Value).Distinct().ToList();
        if (userIds.Count == 0)
        {
            return;
        }

        var role = await db.Set<Role>().FirstOrDefaultAsync(
            r => r.OrganizationId == unit.OrganizationId && r.BuiltInKey == BuiltInRoles.Student, ct);
        if (role is null)
        {
            return;
        }

        // The Student role assignments of these users become exactly one, at the class unit (as IRoleAssignmentSync does).
        var live = await db.Set<RoleAssignment>()
            .Where(a => a.OrganizationId == unit.OrganizationId && a.RoleId == role.Id && userIds.Contains(a.UserId))
            .ToListAsync(ct);
        foreach (var extra in live.Where(a => a.OrgUnitId != unit.Id))
        {
            db.Set<RoleAssignment>().Remove(extra);
        }

        var have = live.Where(a => a.OrgUnitId == unit.Id).Select(a => a.UserId).ToHashSet();
        foreach (var userId in userIds.Where(id => !have.Contains(id)))
        {
            db.Set<RoleAssignment>().Add(new RoleAssignment(unit, userId, role.Id));
        }
    }

    private List<PlacementResult> BuildEnrollments(
        IReadOnlyList<StudentProfile> students, IReadOnlyList<ClassStudent> placements, IReadOnlyList<ClassCourse> courses,
        IReadOnlyDictionary<Guid, IReadOnlyList<(string Type, IReadOnlyList<string> Values)>> restrictions, OrgUnit unit,
        DateOnly on, bool overCapacity)
    {
        var results = new List<PlacementResult>(students.Count);
        var enrollments = new List<CourseEnrollment>(students.Count * Math.Max(1, courses.Count));
        var evaluated = new Dictionary<(string Gender, string Religion, Guid Course), IReadOnlyList<string>>();
        for (var i = 0; i < students.Count; i++)
        {
            var student = students[i];
            var skips = new List<RestrictionSkip>();
            var created = 0;
            foreach (var course in courses)
            {
                // Many students share gender and religion: evaluate each combination once per course.
                var key = (student.Gender, student.Religion, course.CourseId);
                if (!evaluated.TryGetValue(key, out var failed))
                {
                    failed = CourseRestrictionEvaluator.Failed(
                        restrictions.GetValueOrDefault(course.CourseId) ?? [], student.Gender, student.Religion, unit.LevelTypeId);
                    evaluated[key] = failed;
                }

                if (failed.Count > 0)
                {
                    skips.Add(new RestrictionSkip(student.Id, course.Id, failed));
                    continue;
                }

                enrollments.Add(new CourseEnrollment(course, placements[i], EnrollmentSources.Auto, on));
                created++;
            }

            results.Add(new PlacementResult(placements[i], created, skips, overCapacity));
        }

        AddEnrollments(enrollments);
        return results;
    }

    internal void AddEnrollments(IReadOnlyCollection<CourseEnrollment> enrollments)
    {
        if (enrollments.Count == 0)
        {
            return;
        }

        var tracking = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false; // thousands of AddRange calls would each rescan every tracked entity
        try
        {
            db.Set<CourseEnrollment>().AddRange(enrollments);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = tracking;
        }
    }

    /// <summary>Saves; a unique-index race on a live key becomes the matching business conflict instead of a 500.</summary>
    private async Task SaveAsync(string duplicateCode, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new EnrollmentRuleException(duplicateCode);
        }
    }

    /// <summary>Runs in the caller's transaction when there is one, otherwise in its own (rolled back on any failure).</summary>
    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
