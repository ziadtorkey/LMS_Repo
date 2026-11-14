using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Timetable;

/// <summary>The own-data timetable views: a teacher, a substitute, a student and a guardian (M5 Task 15, FR-TT-04, FR-TT-05, L15).</summary>
[Collection(ApiCollection.Name)]
public sealed class TimetableViewTests(LmsApiFactory factory)
{
    private sealed record Setup(AccessTree Tree, TimetableWorld World, TimetableSlot Slot, DateOnly Sunday, Guid MathTeacher);

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var sunday = world.Term.StartDate;
        while (sunday.DayOfWeek != DayOfWeek.Sunday)
        {
            sunday = sunday.AddDays(1);
        }

        TimetableSlot slot;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            slot = new TimetableSlot(world.Class, world.Math.Id, world.Term.Id, world.MathTeacher, DayOfWeek.Sunday, world.First.Id, "Lab");
            slot.Publish();
            db.Add(slot);
            await db.SaveChangesAsync();
        }

        return new Setup(tree, world, slot, sunday, world.MathTeacher);
    }

    private async Task AddExceptionAsync(Setup s, DateOnly date, string kind, Guid? substitute = null, Guid? newPeriod = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = await db.Set<TimetableSlot>().SingleAsync(x => x.Id == s.Slot.Id);
        db.Add(new TimetableException(slot, date, kind, substitute, newPeriod, "private note"));
        await db.SaveChangesAsync();
    }

    private Task<RoleAssignment> TeacherAsync(Setup s, Guid userId) => AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Teacher, userId);

    private async Task<TimetableViewDto> MeAsync(RoleAssignment who, DateOnly from, DateOnly to)
    {
        var response = await SendAsync(factory, HttpMethod.Get, Routes.MyTimetable(from, to), who);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TimetableViewDto>(Web))!;
    }

    [Fact]
    public async Task Substitute_sees_the_slot_on_that_date_only()
    {
        var s = await SetUpAsync("TV1");
        var (_, free) = await AddCourseWithTeacherAsync(factory, s.World.Unit, s.World.Class); // a teacher of the school with no slot
        var substitute = await TeacherAsync(s, free);
        var owner = await TeacherAsync(s, s.MathTeacher);
        var other = await TeacherAsync(s, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var date = s.Sunday.AddDays(7);
        await AddExceptionAsync(s, date, ExceptionKinds.Substitute, free);
        var from = s.Sunday;
        var to = s.Sunday.AddDays(21);

        var asSubstitute = await MeAsync(substitute, from, to);
        var asOwner = await MeAsync(owner, from, to);
        var asOther = await MeAsync(other, from, to);

        var card = Assert.Single(asSubstitute.Cards); // the date of the exception, not the Sundays around it
        Assert.Equal((date, s.Slot.Id, ExceptionKinds.Substitute), (card.Date, card.SlotId, card.ExceptionKind));
        Assert.False(string.IsNullOrEmpty(card.SubstituteName));
        Assert.Equal([s.Sunday, date, s.Sunday.AddDays(14), s.Sunday.AddDays(21)], asOwner.Cards.Select(c => c.Date)); // the teacher of record keeps every Sunday ...
        Assert.Equal(ExceptionKinds.Substitute, asOwner.Cards.Single(c => c.Date == date).ExceptionKind); // ... and sees who covers the one date
        Assert.Empty(asOther.Cards);
    }

    [Fact]
    public async Task Cancelled_and_moved_lessons_show_in_the_views_and_notes_never_do()
    {
        var s = await SetUpAsync("TV2");
        var owner = await TeacherAsync(s, s.MathTeacher);
        await AddExceptionAsync(s, s.Sunday, ExceptionKinds.Cancelled);
        await AddExceptionAsync(s, s.Sunday.AddDays(7), ExceptionKinds.Moved, newPeriod: s.World.Second.Id);

        var view = await MeAsync(owner, s.Sunday, s.Sunday.AddDays(7));

        Assert.Equal([ExceptionKinds.Cancelled, ExceptionKinds.Moved], view.Cards.Select(c => c.ExceptionKind));
        Assert.Equal([s.World.First.Id, s.World.Second.Id], view.Cards.Select(c => c.PeriodId)); // the moved lesson sits in the new period
        Assert.Equal(s.World.Second.Id, view.Periods.Last().Id);
        Assert.DoesNotContain("private note", System.Text.Json.JsonSerializer.Serialize(view)); // the exception note never leaves
    }

    [Fact]
    public async Task A_student_sees_the_own_class_and_ranges_are_validated()
    {
        var s = await SetUpAsync("TV3");
        var studentUser = await PeopleTestKit.AddUserAsync(factory);
        var student = await PeopleTestKit.AddStudentAsync(factory, s.World.Unit, PeopleTestKit.NewCode());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(p => p.Id == student.Id)).LinkUser(studentUser.Id);
            await db.SaveChangesAsync();
        }

        await AcademicTestKit.PlaceAsync(factory, s.World.Class, student);
        var asStudent = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Student, studentUser.Id);

        var view = await MeAsync(asStudent, s.Sunday, s.Sunday.AddDays(6));
        var tooWide = await SendAsync(factory, HttpMethod.Get, Routes.MyTimetable(s.Sunday, s.Sunday.AddDays(31)), asStudent);
        var reversed = await SendAsync(factory, HttpMethod.Get, Routes.MyTimetable(s.Sunday.AddDays(3), s.Sunday), asStudent);

        Assert.Equal(s.Slot.Id, Assert.Single(view.Cards).SlotId);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (tooWide.StatusCode, reversed.StatusCode));
    }

    [Fact]
    public async Task A_guardian_reads_only_their_own_children_and_anyone_else_gets_404()
    {
        var s = await SetUpAsync("TV4");
        var guardianUser = await PeopleTestKit.AddUserAsync(factory);
        var otherGuardianUser = await PeopleTestKit.AddUserAsync(factory);
        var child = await PeopleTestKit.AddStudentAsync(factory, s.World.Unit, PeopleTestKit.NewCode());
        var otherChild = await PeopleTestKit.AddStudentAsync(factory, s.World.Unit, PeopleTestKit.NewCode());
        await AcademicTestKit.PlaceAsync(factory, s.World.Class, child);
        await AcademicTestKit.PlaceAsync(factory, s.World.Class, otherChild);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var guardian = new GuardianProfile(s.Tree.Org.Id, "ولي", "Guardian", PeopleTestKit.NewMobile(), null, null);
            guardian.LinkUser(guardianUser.Id);
            var other = new GuardianProfile(s.Tree.Org.Id, "ولي٢", "Guardian 2", PeopleTestKit.NewMobile(), null, null);
            other.LinkUser(otherGuardianUser.Id);
            db.AddRange(guardian, other);
            db.Add(StudentGuardian.For(await db.Set<StudentProfile>().SingleAsync(p => p.Id == child.Id), guardian, "Father", true));
            db.Add(StudentGuardian.For(await db.Set<StudentProfile>().SingleAsync(p => p.Id == otherChild.Id), other, "Mother", true));
            await db.SaveChangesAsync();
        }

        var guardianRole = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Guardian, guardianUser.Id);
        var teacher = await TeacherAsync(s, s.MathTeacher);
        var range = (s.Sunday, s.Sunday.AddDays(6));

        var own = await SendAsync(factory, HttpMethod.Get, Routes.StudentTimetable(child.Id, range.Item1, range.Item2), guardianRole);
        var someoneElses = await SendAsync(factory, HttpMethod.Get, Routes.StudentTimetable(otherChild.Id, range.Item1, range.Item2), guardianRole);
        var unknown = await SendAsync(factory, HttpMethod.Get, Routes.StudentTimetable(Guid.NewGuid(), range.Item1, range.Item2), guardianRole);
        var asTeacher = await SendAsync(factory, HttpMethod.Get, Routes.StudentTimetable(child.Id, range.Item1, range.Item2), teacher);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(s.Slot.Id, Assert.Single((await own.Content.ReadFromJsonAsync<TimetableViewDto>(Web))!.Cards).SlotId);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode); // another guardian's child
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asTeacher.StatusCode); // a staff member is not a guardian of the child
    }

    [Fact]
    public async Task A_draft_slot_never_reaches_a_view()
    {
        var s = await SetUpAsync("TV5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var draft = new TimetableSlot(s.World.Class, s.World.Science.Id, s.World.Term.Id, s.World.ScienceTeacher, DayOfWeek.Monday, s.World.First.Id, null);
            db.Add(draft);
            await db.SaveChangesAsync();
        }

        var view = await MeAsync(await TeacherAsync(s, s.World.ScienceTeacher), s.Sunday, s.Sunday.AddDays(6));

        Assert.Empty(view.Cards);
    }
}
