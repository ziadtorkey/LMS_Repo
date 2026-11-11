using Lms.Domain.AcademicYears;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Enrollment;

[Collection(ApiCollection.Name)]
public sealed class EnrollmentEngineTests(LmsApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<StudentProfile> NewStudentAsync(EnrollmentWorld w, string? gender = null)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Tree.Root, PeopleTestKit.NewCode());
        if (gender is not null)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, gender, tracked.Religion, tracked.Nationality, null, null, null, null);
            await db.SaveChangesAsync();
        }

        return student;
    }

    private static async Task<StudentProfile> LoadAsync(EngineScope scope, Guid id) =>
        await scope.Db.Set<StudentProfile>().SingleAsync(s => s.Id == id);

    private static async Task<Class> LoadClassAsync(EngineScope scope, Guid id) =>
        await scope.Db.Set<Class>().SingleAsync(c => c.Id == id);

    private async Task<List<CourseEnrollment>> EnrollmentsAsync(Guid studentId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<CourseEnrollment>().AsNoTracking().Where(e => e.StudentId == studentId).ToListAsync();
    }

    private async Task<EnrollmentRuleException> RuleAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<EnrollmentRuleException>(action);
        return ex;
    }

    private async Task SetClassStatusAsync(Guid classId, string status)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<Class>().SingleAsync(c => c.Id == classId)).SetStatus(status);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Placing_student_enrolls_in_all_non_optional_class_courses()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN1");
        var student = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);

        var result = await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);

        Assert.Equal(2, result.EnrollmentsCreated);
        Assert.Empty(result.Skips);
        Assert.False(result.OverCapacity);
        Assert.Equal(ClassStudent.ActiveStatus, result.Placement.Status);
        var enrollments = await EnrollmentsAsync(student.Id);
        Assert.Equal(new[] { w.CcA.Id, w.CcB.Id }.Order(), enrollments.Select(e => e.ClassCourseId).Order()); // not the optional one
        Assert.All(enrollments, e => Assert.Equal((EnrollmentSources.Auto, CourseEnrollment.ActiveStatus, Today), (e.Source, e.Status, e.From)));
        Assert.All(enrollments, e => Assert.Equal(result.Placement.Id, e.ClassStudentId));
        Assert.All(enrollments, e => Assert.Equal(w.Tree.School1.Id, e.OrgUnitId)); // the class unit
    }

    [Fact]
    public async Task Restricted_course_is_skipped_and_reported()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN2");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // The restriction sits on a course owned at the root, above the class unit: it must still apply.
            db.Add(new CourseRestriction(w.B, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        var girl = await NewStudentAsync(w, "female");
        var boy = await NewStudentAsync(w, "male");
        using var scope = new EngineScope(factory.Services);
        var cls = await LoadClassAsync(scope, w.Class.Id);

        var forGirl = await scope.Engine.PlaceAsync(await LoadAsync(scope, girl.Id), cls, Today, false, default);
        var forBoy = await scope.Engine.PlaceAsync(await LoadAsync(scope, boy.Id), cls, Today, false, default);

        Assert.Equal(1, forGirl.EnrollmentsCreated);
        var skip = Assert.Single(forGirl.Skips);
        Assert.Equal((girl.Id, w.CcB.Id), (skip.StudentId, skip.ClassCourseId));
        Assert.Equal([CourseRestrictionEvaluator.Gender], skip.FailedTypes);
        Assert.Equal(w.CcA.Id, Assert.Single(await EnrollmentsAsync(girl.Id)).ClassCourseId);
        Assert.Equal(2, forBoy.EnrollmentsCreated);
        Assert.Empty(forBoy.Skips);
    }

    [Fact]
    public async Task Adding_course_to_class_enrolls_current_students()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN3");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(w.Optional, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        var boy = await NewStudentAsync(w, "male");
        var girl = await NewStudentAsync(w, "female");
        using (var place = new EngineScope(factory.Services))
        {
            var cls = await LoadClassAsync(place, w.Class.Id);
            await place.Engine.PlaceAsync(await LoadAsync(place, boy.Id), cls, Today, false, default);
            await place.Engine.PlaceAsync(await LoadAsync(place, girl.Id), cls, Today, false, default);
        }

        var late = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        using var scope = new EngineScope(factory.Services);
        var cc = new ClassCourse(await LoadClassAsync(scope, w.Class.Id), late, false);
        scope.Db.Add(cc);
        await scope.Handler.OnAddedAsync(cc, default);

        Assert.Contains(await EnrollmentsAsync(boy.Id), e => e.ClassCourseId == cc.Id);
        Assert.Contains(await EnrollmentsAsync(girl.Id), e => e.ClassCourseId == cc.Id);

        // The optional course becomes mandatory: the restricted student is skipped, the other is enrolled.
        var optional = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);
        optional.SetOptional(false);
        await scope.Handler.OnOptionalChangedAsync(optional, default);
        Assert.Contains(await EnrollmentsAsync(boy.Id), e => e.ClassCourseId == w.CcOptional.Id);
        Assert.DoesNotContain(await EnrollmentsAsync(girl.Id), e => e.ClassCourseId == w.CcOptional.Id);

        // Becoming optional again keeps the existing enrollments.
        optional.SetOptional(true);
        await scope.Handler.OnOptionalChangedAsync(optional, default);
        Assert.Contains(await EnrollmentsAsync(boy.Id), e => e.ClassCourseId == w.CcOptional.Id && e.Status == CourseEnrollment.ActiveStatus);
    }

    [Fact]
    public async Task Optional_course_needs_manual_enrollment_and_manual_on_mandatory_is_409()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN4");
        var student = await NewStudentAsync(w);
        var outsider = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        var s = await LoadAsync(scope, student.Id);
        var optional = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);
        var mandatory = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcA.Id);
        Assert.DoesNotContain(await EnrollmentsAsync(student.Id), e => e.ClassCourseId == optional.Id); // not automatic

        var enrolled = await scope.Engine.EnrollManualAsync(optional, s, EnrollmentSources.Manual, default);

        Assert.Equal((EnrollmentSources.Manual, CourseEnrollment.ActiveStatus), (enrolled.Source, enrolled.Status));
        Assert.Equal(ProblemCodes.already_enrolled, (await RuleAsync(() => scope.Engine.EnrollManualAsync(optional, s, EnrollmentSources.Manual, default))).Code);
        var onMandatory = await RuleAsync(() => scope.Engine.EnrollManualAsync(mandatory, s, EnrollmentSources.Manual, default));
        Assert.Equal((ProblemCodes.enrollment_not_optional, 409), (onMandatory.Code, onMandatory.StatusCode));
        var notPlaced = await RuleAsync(async () => await scope.Engine.EnrollManualAsync(optional, await LoadAsync(scope, outsider.Id), EnrollmentSources.Manual, default));
        Assert.Equal((ProblemCodes.not_in_class, 400), (notPlaced.Code, notPlaced.StatusCode));
    }

    [Fact]
    public async Task Manual_enrollment_checks_restrictions_unless_exceptional()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(w.Optional, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        var girl = await NewStudentAsync(w, "female");
        using var scope = new EngineScope(factory.Services);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, girl.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        var student = await LoadAsync(scope, girl.Id);
        var optional = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);

        var refused = await RuleAsync(() => scope.Engine.EnrollManualAsync(optional, student, EnrollmentSources.Manual, default));
        Assert.Equal(ProblemCodes.restriction_not_met, refused.Code);
        Assert.Equal([CourseRestrictionEvaluator.Gender], refused.FailedTypes);

        var admitted = await scope.Engine.EnrollManualAsync(optional, student, EnrollmentSources.Exceptional, default);
        Assert.Equal(EnrollmentSources.Exceptional, admitted.Source);
    }

    [Fact]
    public async Task Second_placement_in_same_year_is_409_already_placed()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN6");
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School2, w.Year);
        var student = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);

        var again = await RuleAsync(async () => await PlaceAsync(scope, student.Id, await LoadClassAsync(scope, other.Id), false));

        Assert.Equal(ProblemCodes.already_placed, again.Code);
    }

    [Fact]
    public async Task Concurrent_placements_of_one_student_leave_exactly_one_placement()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN7");
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School2, w.Year);
        var student = await NewStudentAsync(w);

        async Task<bool> TryPlaceAsync(Guid classId)
        {
            using var scope = new EngineScope(factory.Services);
            try
            {
                await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, classId), Today, false, default);
                return true;
            }
            catch (EnrollmentRuleException ex) when (ex.Code == ProblemCodes.already_placed)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(TryPlaceAsync(w.Class.Id), TryPlaceAsync(other.Id));

        Assert.Single(outcomes, ok => ok);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<ClassStudent>().CountAsync(c => c.StudentId == student.Id && c.Status == ClassStudent.ActiveStatus));
    }

    [Fact]
    public async Task Block_mode_full_class_is_409_and_warn_mode_needs_confirmation()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN8");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).Update("CAP", "فصل", "Cap class", 1, null);
            await db.SaveChangesAsync();
        }

        var first = await NewStudentAsync(w);
        var second = await NewStudentAsync(w);
        var third = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);
        var cls = await LoadClassAsync(scope, w.Class.Id);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, first.Id), cls, Today, false, default);

        // Default mode is Warn: refused until confirmed, then placed and flagged.
        Assert.Equal(ProblemCodes.over_capacity, (await RuleAsync(() => PlaceAsync(scope, second.Id, cls, false))).Code);
        var confirmed = await PlaceAsync(scope, second.Id, cls, true);
        Assert.True(confirmed.OverCapacity);

        // Block mode: full is full, confirmation does not help.
        await scope.Settings.SetAsync(SettingKeys.ClassesCapacityMode, w.Tree.School1.Id, "Block", false);
        var blocked = await RuleAsync(() => PlaceAsync(scope, third.Id, cls, true));
        Assert.Equal((ProblemCodes.class_full, 409), (blocked.Code, blocked.StatusCode));
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await read.Set<ClassStudent>().AnyAsync(c => c.StudentId == third.Id));
    }

    private static async Task<PlacementResult> PlaceAsync(EngineScope scope, Guid studentId, Class cls, bool confirm) =>
        await scope.Engine.PlaceAsync(await LoadAsync(scope, studentId), cls, Today, confirm, default);

    [Fact]
    public async Task Concurrent_placements_into_the_last_seat_admit_only_one_in_block_mode()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN9");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).Update("LAST", "فصل", "Last seat", 1, null);
            await db.AddAsync(new Lms.Domain.Configuration.OrgUnitSetting(
                w.Tree.Org.Id, w.Tree.School1.Id, w.Tree.School1.Path, SettingKeys.ClassesCapacityMode.Name, "\"Block\"", false));
            await db.SaveChangesAsync();
        }

        var students = new[] { await NewStudentAsync(w), await NewStudentAsync(w), await NewStudentAsync(w) };

        async Task<bool> TryAsync(Guid studentId)
        {
            using var scope = new EngineScope(factory.Services);
            try
            {
                await scope.Engine.PlaceAsync(await LoadAsync(scope, studentId), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
                return true;
            }
            catch (EnrollmentRuleException ex) when (ex.Code == ProblemCodes.class_full)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(students.Select(s => TryAsync(s.Id)));

        Assert.Single(outcomes, ok => ok);
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await read.Set<ClassStudent>().CountAsync(c => c.ClassId == w.Class.Id && c.Status == ClassStudent.ActiveStatus));
    }

    [Fact]
    public async Task Placement_moves_home_unit_guardian_links_and_student_role()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN10");
        var student = await NewStudentAsync(w); // home: the root
        var user = await PeopleTestKit.AddUserAsync(factory);
        var guardian = await PeopleTestKit.AddGuardianAsync(factory, w.Tree.Org.Id, PeopleTestKit.NewMobile());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.LinkUser(user.Id);
            db.Add(StudentGuardian.For(tracked, await db.Set<GuardianProfile>().SingleAsync(g => g.Id == guardian.Id), "father", true));
            await db.SaveChangesAsync();
        }

        var atRoot = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.Student, user.Id);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        }

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var moved = await read.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
        Assert.Equal((w.Tree.School1.Id, w.Tree.School1.Path), (moved.OrgUnitId, moved.OrgUnitPath));
        var link = await read.Set<StudentGuardian>().SingleAsync(l => l.StudentId == student.Id);
        Assert.Equal(w.Tree.School1.Id, link.OrgUnitId);
        var assignments = await read.RoleAssignments.Where(a => a.UserId == user.Id).ToListAsync();
        var current = Assert.Single(assignments);
        Assert.Equal(w.Tree.School1.Id, current.OrgUnitId); // one Student assignment, at the class unit
        Assert.NotEqual(atRoot.Id, current.Id);
    }

    [Fact]
    public async Task Drop_keeps_history_and_removes_group_membership()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN11");
        var student = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        var enrollment = await scope.Db.Set<CourseEnrollment>().SingleAsync(e => e.StudentId == student.Id && e.ClassCourseId == w.CcA.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var group = new StudentGroup(w.CcA, "مجموعة", "Group", null, null);
            db.AddRange(group, new GroupMember(group, enrollment));
            await db.SaveChangesAsync();
        }

        await scope.Engine.DropAsync(enrollment, "Changed mind", Today, default);
        await scope.Engine.DropAsync(enrollment, "Again", Today, default); // dropping twice changes nothing

        var kept = Assert.Single(await EnrollmentsAsync(student.Id), e => e.Id == enrollment.Id);
        Assert.Equal((CourseEnrollment.DroppedStatus, "Changed mind", Today), (kept.Status, kept.DropReason, kept.To));
        Assert.Null(kept.ActiveKey);
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await read.Set<GroupMember>().Where(m => m.EnrollmentId == enrollment.Id).ToListAsync());
        // Re-enrolling is possible again after a drop.
        var cc = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);
        Assert.Equal(CourseEnrollment.ActiveStatus, (await scope.Engine.EnrollManualAsync(cc, await LoadAsync(scope, student.Id), EnrollmentSources.Manual, default)).Status);
    }

    [Fact]
    public async Task Removing_a_student_from_the_class_drops_everything_and_frees_the_year()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN12");
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School2, w.Year);
        var student = await NewStudentAsync(w);
        ClassStudent placement;
        using (var scope = new EngineScope(factory.Services))
        {
            placement = (await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default)).Placement;
            await scope.Engine.RemoveFromClassAsync(placement, "Left school", Today, default);
        }

        var enrollments = await EnrollmentsAsync(student.Id);
        Assert.NotEmpty(enrollments);
        Assert.All(enrollments, e => Assert.Equal(CourseEnrollment.DroppedStatus, e.Status));
        await using (var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var ended = await read.Set<ClassStudent>().SingleAsync(c => c.Id == placement.Id);
            Assert.Equal((ClassStudent.RemovedStatus, "Left school"), (ended.Status, ended.EndReason));
            Assert.Null(ended.ActiveYearKey);
        }

        // The slot of that year is free: the student can be placed in another class.
        using var again = new EngineScope(factory.Services);
        var result = await again.Engine.PlaceAsync(await LoadAsync(again, student.Id), await LoadClassAsync(again, other.Id), Today, false, default);
        Assert.Equal(ClassStudent.ActiveStatus, result.Placement.Status);
    }

    [Fact]
    public async Task Removing_course_with_manual_enrollment_is_409_otherwise_auto_enrollments_dropped()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN13");
        var student = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);
        await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        var optional = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);
        var mandatory = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcA.Id);
        await scope.Engine.EnrollManualAsync(optional, await LoadAsync(scope, student.Id), EnrollmentSources.Manual, default);

        var vetoed = await Assert.ThrowsAsync<ClassCourseInUseException>(() => scope.Handler.OnRemovingAsync(optional, default));
        Assert.Equal("enrollments", vetoed.Reason);
        Assert.Contains(await EnrollmentsAsync(student.Id), e => e.ClassCourseId == optional.Id && e.Status == CourseEnrollment.ActiveStatus);

        await scope.Handler.OnRemovingAsync(mandatory, default); // only Auto enrollments: dropped, history kept
        var dropped = Assert.Single(await EnrollmentsAsync(student.Id), e => e.ClassCourseId == mandatory.Id);
        Assert.Equal(CourseEnrollment.DroppedStatus, dropped.Status);

        // A group member also blocks.
        var autoCourse = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcB.Id);
        var enrollment = await scope.Db.Set<CourseEnrollment>().SingleAsync(e => e.StudentId == student.Id && e.ClassCourseId == autoCourse.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var group = new StudentGroup(autoCourse, "مجموعة", "Group", null, null);
            db.AddRange(group, new GroupMember(group, enrollment));
            await db.SaveChangesAsync();
        }

        Assert.Equal("groups", (await Assert.ThrowsAsync<ClassCourseInUseException>(() => scope.Handler.OnRemovingAsync(autoCourse, default))).Reason);
    }

    [Fact]
    public async Task Closed_year_and_closed_class_block_every_write_409()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN14");
        var student = await NewStudentAsync(w);
        var late = await NewStudentAsync(w);
        ClassStudent placement;
        CourseEnrollment enrollment;
        using (var scope = new EngineScope(factory.Services))
        {
            placement = (await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default)).Placement;
            enrollment = await scope.Db.Set<CourseEnrollment>().FirstAsync(e => e.StudentId == student.Id);
        }

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == w.Year.Id)).Close();
            await db.SaveChangesAsync();
        }

        using var closed = new EngineScope(factory.Services);
        var cls = await LoadClassAsync(closed, w.Class.Id);
        var cc = await closed.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);
        var live = await LoadAsync(closed, student.Id);
        var tracked = await closed.Db.Set<CourseEnrollment>().SingleAsync(e => e.Id == enrollment.Id);
        var trackedPlacement = await closed.Db.Set<ClassStudent>().SingleAsync(c => c.Id == placement.Id);

        foreach (var write in new Func<Task>[]
                 {
                     async () => await closed.Engine.PlaceAsync(await LoadAsync(closed, late.Id), cls, Today, false, default),
                     () => closed.Engine.EnrollManualAsync(cc, live, EnrollmentSources.Manual, default),
                     () => closed.Engine.DropAsync(tracked, "x", Today, default),
                     () => closed.Engine.RemoveFromClassAsync(trackedPlacement, "x", Today, default),
                 })
        {
            Assert.Equal(ProblemCodes.year_closed, (await RuleAsync(write)).Code);
        }

        // A closed class (year open again) is refused too.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var year = await db.Set<AcademicYear>().SingleAsync(y => y.Id == w.Year.Id);
            db.Entry(year).Property(nameof(AcademicYear.Status)).CurrentValue = AcademicYearStatus.Active;
            await db.SaveChangesAsync();
        }

        await SetClassStatusAsync(w.Class.Id, Class.ClosedStatus);
        using var inClosedClass = new EngineScope(factory.Services);
        Assert.Equal(ProblemCodes.class_closed,
            (await RuleAsync(async () => await inClosedClass.Engine.PlaceAsync(
                await LoadAsync(inClosedClass, late.Id), await LoadClassAsync(inClosedClass, w.Class.Id), Today, false, default))).Code);
    }

    [Fact]
    public async Task Inactive_student_cannot_be_placed()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN15");
        var student = await NewStudentAsync(w);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id)).SetStatus(PersonStatus.Deactivated);
            await db.SaveChangesAsync();
        }

        using var scope = new EngineScope(factory.Services);

        var refused = await RuleAsync(() => PlaceAsync(scope, student.Id, scope.Db.Set<Class>().Single(c => c.Id == w.Class.Id), false));

        Assert.Equal(400, refused.StatusCode);
    }

    [Fact]
    public async Task Manual_enrollment_of_a_deactivated_student_is_refused()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN18");
        var student = await NewStudentAsync(w);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);
        }

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id)).SetStatus(PersonStatus.Deactivated);
            await db.SaveChangesAsync();
        }

        using var again = new EngineScope(factory.Services);
        var optional = await again.Db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcOptional.Id);

        var refused = await RuleAsync(async () => await again.Engine.EnrollManualAsync(optional, await LoadAsync(again, student.Id), EnrollmentSources.Manual, default));

        Assert.Equal(400, refused.StatusCode);
        Assert.DoesNotContain(await EnrollmentsAsync(student.Id), e => e.ClassCourseId == optional.Id);
    }

    [Fact]
    public async Task Enrollment_rows_are_audited()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN16");
        var student = await NewStudentAsync(w);
        using var scope = new EngineScope(factory.Services);

        await scope.Engine.PlaceAsync(await LoadAsync(scope, student.Id), await LoadClassAsync(scope, w.Class.Id), Today, false, default);

        var ids = (await EnrollmentsAsync(student.Id)).Select(e => e.Id).ToList();
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(ids.Count, await db.AuditEntries.CountAsync(e => e.EntityType == nameof(CourseEnrollment) && ids.Contains(e.EntityId) && e.Action == AuditAction.Created));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(ClassStudent) && e.Action == AuditAction.Created));
    }

    [Fact]
    public async Task Whole_batch_is_refused_when_one_student_is_already_placed()
    {
        var w = await EnrollmentWorld.CreateAsync(factory, "EN17");
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School2, w.Year);
        var placed = await NewStudentAsync(w);
        var fresh = await NewStudentAsync(w);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Engine.PlaceAsync(await LoadAsync(scope, placed.Id), await LoadClassAsync(scope, other.Id), Today, false, default);
        }

        using var batch = new EngineScope(factory.Services);
        var students = new[] { await LoadAsync(batch, fresh.Id), await LoadAsync(batch, placed.Id) };

        var targetClass = await LoadClassAsync(batch, w.Class.Id);
        var refused = await RuleAsync(() => batch.Engine.PlaceManyAsync(students, targetClass));

        Assert.Equal(ProblemCodes.already_placed, refused.Code);
        Assert.Empty(await EnrollmentsAsync(fresh.Id)); // all or nothing
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await read.Set<ClassStudent>().AnyAsync(c => c.StudentId == fresh.Id));
    }
}

internal static class EnrollmentEngineExtensions
{
    public static Task<IReadOnlyList<PlacementResult>> PlaceManyAsync(
        this IEnrollmentEngine engine, IReadOnlyList<StudentProfile> students, Class scope) =>
        engine.PlaceManyAsync(students, scope, DateOnly.FromDateTime(DateTime.UtcNow), false, default);
}
