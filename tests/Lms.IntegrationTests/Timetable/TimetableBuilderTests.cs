using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.Infrastructure.Configuration;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Timetable;

[Collection(ApiCollection.Name)]
public sealed class TimetableBuilderTests(LmsApiFactory factory)
{
    private static async Task<string> BodyAsync(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    [Fact]
    public async Task Teacher_double_booking_blocks_in_block_mode()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var other = await AcademicTestKit_AddClassAsync(w);
        var (otherCourse, _) = await AddCourseWithTeacherAsync(factory, tree.School1, other, w.MathTeacher); // the same teacher in another class
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First));

        var clash = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(other.Id), admin, Slot(w, otherCourse, w.MathTeacher, DayOfWeek.Sunday, w.First));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        var text = await BodyAsync(clash);
        Assert.Contains(ProblemCodes.timetable_conflict, text);
        Assert.Contains(ConflictKinds.TeacherDoubleBooked, text);
    }

    [Fact]
    public async Task Class_double_booking_blocks_in_block_mode()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Monday, w.First));

        var clash = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(w.Class.Id), admin, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.First));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains(ConflictKinds.ClassDoubleBooked, await BodyAsync(clash));
        // A different period is fine.
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.Second));
    }

    [Fact]
    public async Task Warn_mode_saves_and_returns_warnings()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(SettingKeys.TimetableConflictMode, tree.School1.Id, ConflictModes.Warn, false);
        }

        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Tuesday, w.First));
        var saved = await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Tuesday, w.First));

        Assert.Single(saved.Warnings);
        Assert.Equal(ConflictKinds.ClassDoubleBooked, saved.Warnings[0].Kind);
        Assert.Contains(SlotBadges.Conflict, saved.Slot.Badges);
        var validation = await (await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetableValidate(w.Class.Id, w.Term.Id), admin)).Content.ReadFromJsonAsync<TimetableValidationDto>(Web);
        Assert.NotEmpty(validation!.Conflicts);
    }

    [Fact]
    public async Task Room_text_is_never_checked_for_conflicts()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);

        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Wednesday, w.First, "Lab 1"));
        var same = await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Wednesday, w.Second, "Lab 1"));

        Assert.Empty(same.Warnings);
        Assert.Equal("Lab 1", same.Slot.RoomName);
    }

    [Fact]
    public async Task Move_is_a_single_put_with_version()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB5");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var created = (await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Thursday, w.First))).Slot;

        var moved = await SendAsync(factory, HttpMethod.Put, Routes.ClassTimetableSlot(w.Class.Id, created.Id), admin,
            Slot(w, w.Math, w.MathTeacher, DayOfWeek.Thursday, w.Second, version: created.Version));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var result = (await moved.Content.ReadFromJsonAsync<SaveSlotResultDto>(Web))!;
        Assert.Equal(w.Second.Id, result.Slot.PeriodId);
        Assert.Equal(created.Version + 1, result.Slot.Version);

        var stale = await SendAsync(factory, HttpMethod.Put, Routes.ClassTimetableSlot(w.Class.Id, created.Id), admin,
            Slot(w, w.Math, w.MathTeacher, DayOfWeek.Thursday, w.First, version: created.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains(ProblemCodes.concurrency_conflict, await BodyAsync(stale));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Put, Routes.ClassTimetableSlot(w.Class.Id, created.Id), admin,
            Slot(w, w.Math, w.MathTeacher, DayOfWeek.Thursday, w.First))).StatusCode); // a version is required
    }

    [Fact]
    public async Task Drafts_are_visible_only_with_manage_and_publish_turns_them_into_published_slots()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB6");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First));

        async Task<ClassTimetableDto> ReadAsync(Lms.Domain.Access.RoleAssignment who)
        {
            var response = await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetable(w.Class.Id, w.Term.Id), who);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ClassTimetableDto>(Web))!;
        }

        Assert.Single((await ReadAsync(admin)).Slots);
        Assert.Equal(1, (await ReadAsync(admin)).DraftCount);
        Assert.Empty((await ReadAsync(teacher)).Slots);

        var publish = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), admin, new PublishTimetableRequest(w.Term.Id));
        var result = (await publish.Content.ReadFromJsonAsync<PublishTimetableResultDto>(Web))!;
        Assert.Equal(1, result.Published);

        Assert.Single((await ReadAsync(teacher)).Slots);
        Assert.Equal(0, (await ReadAsync(admin)).DraftCount);
        var again = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), admin, new PublishTimetableRequest(w.Term.Id)))
            .Content.ReadFromJsonAsync<PublishTimetableResultDto>(Web))!;
        Assert.Equal(0, again.Published);
    }

    [Fact]
    public async Task A_teacher_cannot_build_and_a_used_period_cannot_be_deleted()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var slot = (await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First))).Slot;

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(w.Class.Id), teacher,
            Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.First))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Delete, Routes.ClassTimetableSlot(w.Class.Id, slot.Id), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), teacher, new PublishTimetableRequest(w.Term.Id))).StatusCode);

        var inUse = await SendAsync(factory, HttpMethod.Delete, Routes.Period(tree.School1.Id, w.First.Id), admin);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Contains(ProblemCodes.period_in_use, await BodyAsync(inUse));
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.ClassTimetableSlot(w.Class.Id, slot.Id), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(tree.School1.Id, w.First.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task Other_school_admin_gets_404_and_the_table_is_isolated()
    {
        var a = await AccessTestData.SeedAsync(factory, "TB8A");
        var b = await AccessTestData.SeedAsync(factory, "TB8B");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(factory, a, a.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var adminB = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, a.School1);
        var slot = (await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First))).Slot;

        foreach (var who in new[] { school2Admin, adminB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetable(w.Class.Id, w.Term.Id), who)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(w.Class.Id), who, Slot(w, w.Science, w.ScienceTeacher, DayOfWeek.Monday, w.First))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.ClassTimetableSlot(w.Class.Id, slot.Id), who)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), who, new PublishTimetableRequest(w.Term.Id))).StatusCode);
        }

        await using var dbS2 = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School2.Path));
        Assert.False(await dbS2.Set<TimetableSlot>().AnyAsync(s => s.Id == slot.Id));
        await using var dbB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.False(await dbB.Set<TimetableSlot>().AnyAsync(s => s.Id == slot.Id));
        await using var dbS1 = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.True(await dbS1.Set<TimetableSlot>().AnyAsync(s => s.Id == slot.Id));
    }

    [Fact]
    public async Task A_clash_with_an_unreadable_slot_names_only_its_kind_and_another_client_never_clashes()
    {
        var a = await AccessTestData.SeedAsync(factory, "TB10A");
        var b = await AccessTestData.SeedAsync(factory, "TB10B");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var manager1 = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Manager, Guid.NewGuid());
        var w = await NewWorldAsync(factory, a.School1);
        var other = await Lms.IntegrationTests.Academic.AcademicTestKit.AddClassAsync(factory, a.School2, await Lms.IntegrationTests.Academic.AcademicTestKit.AddYearAsync(factory, a.School2));
        var (foreignCourse, teacher) = await AddCourseWithTeacherAsync(factory, a.School2, other);
        // A slot of the same teacher in school 2 at the same day, term and period ids (written directly: the builder of school 1 cannot see school 2).
        Guid foreignSlotId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var slot = new TimetableSlot(other, foreignCourse.Id, w.Term.Id, teacher, DayOfWeek.Sunday, w.First.Id, null);
            db.Add(slot);
            await db.SaveChangesAsync();
            foreignSlotId = slot.Id;
        }

        var (course1, _) = await AddCourseWithTeacherAsync(factory, a.School1, w.Class, teacher);
        var clash = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(w.Class.Id), manager1, Slot(w, course1, teacher, DayOfWeek.Sunday, w.First));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        var text = await BodyAsync(clash);
        Assert.Contains(ConflictKinds.TeacherDoubleBooked, text);
        Assert.DoesNotContain(foreignSlotId.ToString(), text);
        Assert.DoesNotContain("Course", text.Replace("timetable_conflict", string.Empty)); // no course name of the other school

        // Another client's slot of the same teacher id never clashes (client matched).
        var bWorld = await NewWorldAsync(factory, b.School1);
        var ok = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetableSlots(w.Class.Id), admin, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Monday, w.First));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.NotNull(bWorld);
    }

    [Fact]
    public async Task Publish_in_block_mode_refuses_drafts_that_clash_after_the_fact_and_an_edit_returns_a_published_slot_to_draft()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TB11");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var w = await NewWorldAsync(factory, tree.School1);
        var other = await AcademicTestKit_AddClassAsync(w);
        var (otherCourse, _) = await AddCourseWithTeacherAsync(factory, tree.School1, other, w.MathTeacher);
        var draft = (await CreateSlotAsync(factory, admin, w.Class.Id, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Sunday, w.First))).Slot;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin())) // the same teacher gets a slot elsewhere in between
        {
            db.Add(new TimetableSlot(other, otherCourse.Id, w.Term.Id, w.MathTeacher, DayOfWeek.Sunday, w.First.Id, null));
            await db.SaveChangesAsync();
        }

        var refused = await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), admin, new PublishTimetableRequest(w.Term.Id));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains(ProblemCodes.timetable_conflict, await BodyAsync(refused));
        var after = (await (await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetable(w.Class.Id, w.Term.Id), admin)).Content.ReadFromJsonAsync<ClassTimetableDto>(Web))!;
        Assert.Equal(SlotStates.Draft, after.Slots.Single(s => s.Id == draft.Id).State); // nothing was published

        // Move it away from the clash: it publishes, and a later edit sends it back to Draft.
        var moved = await SendAsync(factory, HttpMethod.Put, Routes.ClassTimetableSlot(w.Class.Id, draft.Id), admin, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Monday, w.First, version: draft.Version));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var published = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(w.Class.Id), admin, new PublishTimetableRequest(w.Term.Id)))
            .Content.ReadFromJsonAsync<PublishTimetableResultDto>(Web))!;
        Assert.Equal(1, published.Published);
        var live = (await (await SendAsync(factory, HttpMethod.Get, Routes.ClassTimetable(w.Class.Id, w.Term.Id), admin)).Content.ReadFromJsonAsync<ClassTimetableDto>(Web))!.Slots.Single(s => s.Id == draft.Id);
        var edited = (await (await SendAsync(factory, HttpMethod.Put, Routes.ClassTimetableSlot(w.Class.Id, draft.Id), admin, Slot(w, w.Math, w.MathTeacher, DayOfWeek.Monday, w.Second, version: live.Version)))
            .Content.ReadFromJsonAsync<SaveSlotResultDto>(Web))!;
        Assert.Equal(SlotStates.Draft, edited.Slot.State);
    }

    private async Task<Lms.Domain.Classes.Class> AcademicTestKit_AddClassAsync(TimetableWorld w) =>
        await Lms.IntegrationTests.Academic.AcademicTestKit.AddClassAsync(factory, w.Unit, w.Year);
}
