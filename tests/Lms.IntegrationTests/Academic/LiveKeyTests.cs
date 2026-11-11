using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Academic;

/// <summary>Invariants among live rows are kept by the database through live keys (MySQL has no partial unique index).</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveKeyTests(LmsApiFactory factory)
{
    private static readonly DateOnly Start = new(2041, 9, 1);

    private async Task SoftDeleteAsync<T>(T entity) where T : class
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Attach(entity);
        db.Remove(entity);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Soft_deleted_course_frees_its_code()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK1");
        var code = AcademicTestKit.NewCode();
        var first = await AcademicTestKit.AddCourseAsync(factory, tree.School1, code);

        // live duplicates collide, whatever the case and the unit
        await Assert.ThrowsAsync<DbUpdateException>(() => AcademicTestKit.AddCourseAsync(factory, tree.School2, code.ToLowerInvariant()));

        await SoftDeleteAsync(first);

        await AcademicTestKit.AddCourseAsync(factory, tree.School2, code);
    }

    [Fact]
    public async Task Course_code_is_reusable_in_another_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "LK2A");
        var b = await AccessTestData.SeedAsync(factory, "LK2B");
        var code = AcademicTestKit.NewCode();
        await AcademicTestKit.AddCourseAsync(factory, a.Root, code);

        await AcademicTestKit.AddCourseAsync(factory, b.Root, code);
    }

    [Fact]
    public async Task Class_code_is_unique_per_academic_year_among_live_classes()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK3");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var otherYear = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var code = AcademicTestKit.NewCode();
        var first = await AcademicTestKit.AddClassAsync(factory, tree.School1, year, code);

        await Assert.ThrowsAsync<DbUpdateException>(() => AcademicTestKit.AddClassAsync(factory, tree.School2, year, code));
        await AcademicTestKit.AddClassAsync(factory, tree.School2, otherYear, code);

        await SoftDeleteAsync(first);
        await AcademicTestKit.AddClassAsync(factory, tree.School2, year, code);
    }

    [Fact]
    public async Task Second_active_class_for_same_student_and_year_violates_unique()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK4");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var classA = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var classB = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        await AcademicTestKit.PlaceAsync(factory, classA, student);

        await Assert.ThrowsAsync<DbUpdateException>(() => AcademicTestKit.PlaceAsync(factory, classB, student));

        // a different year is a different slot
        var nextYear = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await AcademicTestKit.PlaceAsync(factory, await AcademicTestKit.AddClassAsync(factory, tree.School1, nextYear), student);
    }

    [Fact]
    public async Task Removed_placement_frees_the_slot()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK5");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var classA = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var classB = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var placement = await AcademicTestKit.PlaceAsync(factory, classA, student);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<ClassStudent>().SingleAsync(p => p.Id == placement.Id);
            tracked.End(ClassStudent.RemovedStatus, Start, "left");
            await db.SaveChangesAsync();
        }

        await AcademicTestKit.PlaceAsync(factory, classB, student);
    }

    [Fact]
    public async Task Student_cannot_be_in_two_groups_of_one_class_course_even_concurrently()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK6");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.School1);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var placement = await AcademicTestKit.PlaceAsync(factory, cls, student);
        StudentGroup g1, g2;
        CourseEnrollment enrollment;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            enrollment = new CourseEnrollment(classCourse, placement, "Auto", Start);
            g1 = new StudentGroup(classCourse, "أ", "A", null, null);
            g2 = new StudentGroup(classCourse, "ب", "B", null, null);
            db.AddRange(enrollment, g1, g2);
            await db.SaveChangesAsync();
        }

        async Task<bool> TryJoinAsync(StudentGroup group)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            db.Add(new GroupMember(group, enrollment));
            try
            {
                await db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryJoinAsync(g1), TryJoinAsync(g2));

        Assert.Equal(1, results.Count(r => r));
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await check.Set<GroupMember>().CountAsync(m => m.ClassCourseId == classCourse.Id));
    }

    [Fact]
    public async Task One_open_primary_teacher_per_class_course_and_one_live_enrollment()
    {
        var tree = await AccessTestData.SeedAsync(factory, "LK7");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.School1);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var placement = await AcademicTestKit.PlaceAsync(factory, cls, student);
        var teacher1 = await PeopleTestKit.AddUserAsync(factory);
        var teacher2 = await PeopleTestKit.AddUserAsync(factory);

        async Task AddAsync(params object[] rows)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            db.AddRange(rows);
            await db.SaveChangesAsync();
        }

        var first = new ClassCourseTeacher(classCourse, teacher1.Id, ClassCourseTeacher.PrimaryRole, Start);
        await AddAsync(first, new ClassCourseTeacher(classCourse, teacher2.Id, ClassCourseTeacher.AssistantRole, Start));
        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(new ClassCourseTeacher(classCourse, teacher2.Id, ClassCourseTeacher.PrimaryRole, Start)));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<ClassCourseTeacher>().SingleAsync(t => t.Id == first.Id)).End(Start);
            await db.SaveChangesAsync();
        }

        await AddAsync(new ClassCourseTeacher(classCourse, teacher2.Id, ClassCourseTeacher.PrimaryRole, Start.AddDays(1)));

        await AddAsync(new CourseEnrollment(classCourse, placement, "Auto", Start));
        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(new CourseEnrollment(classCourse, placement, "Auto", Start)));
    }
}
