using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Timetable;

/// <summary>Dated exceptions of a published slot: substitute, cancelled, moved; and the substitute relationship (M5 Task 15, FR-TT-05, L15).</summary>
[Collection(ApiCollection.Name)]
public sealed class ExceptionTests(LmsApiFactory factory)
{
    private sealed record Setup(Access.AccessTree Tree, RoleAssignment Admin, TimetableWorld World, TimetableSlot Slot, DateOnly Date);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>A published Sunday first-period slot of the Math teacher, and the first Sunday of the term after its start.</summary>
    private async Task<Setup> SetUpAsync(string name, bool publish = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var world = await NewWorldAsync(factory, tree.School1);
        TimetableSlot slot;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            slot = new TimetableSlot(world.Class, world.Math.Id, world.Term.Id, world.MathTeacher, DayOfWeek.Sunday, world.First.Id, "Lab 2");
            if (publish)
            {
                slot.Publish();
            }

            db.Add(slot);
            await db.SaveChangesAsync();
        }

        var date = world.Term.StartDate.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return new Setup(tree, admin, world, slot, date);
    }

    private Task<HttpResponseMessage> PostAsync(Setup s, SaveTimetableExceptionRequest body, RoleAssignment? who = null) =>
        SendAsync(factory, HttpMethod.Post, Routes.TimetableSlotExceptions(s.Slot.Id), who ?? s.Admin, body);

    private static SaveTimetableExceptionRequest Cancel(DateOnly date) => new(date, ExceptionKinds.Cancelled, null, null, null);

    [Fact]
    public async Task Exception_never_changes_the_base_slot()
    {
        var s = await SetUpAsync("TX1");

        var created = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Moved, null, s.World.Second.Id, "assembly"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = await db.Set<TimetableSlot>().SingleAsync(x => x.Id == s.Slot.Id);
        Assert.Equal((s.World.First.Id, DayOfWeek.Sunday, s.World.MathTeacher, s.Slot.Version), (slot.PeriodId, slot.Day, slot.TeacherUserId, slot.Version));
        var dto = (await created.Content.ReadFromJsonAsync<TimetableExceptionDto>(Web))!;
        Assert.Equal((ExceptionKinds.Moved, s.World.Second.Id, "assembly"), (dto.Kind, dto.NewPeriodId, dto.Note));
    }

    [Fact]
    public async Task Exception_on_a_wrong_weekday_a_non_working_day_or_outside_the_term_is_refused()
    {
        var s = await SetUpAsync("TX2");

        var monday = await PostAsync(s, Cancel(s.Date.AddDays(1))); // the slot is on Sundays
        var friday = await PostAsync(s, Cancel(s.Date.AddDays(5))); // not a Sunday either
        var beforeTerm = await PostAsync(s, Cancel(s.World.Term.StartDate.AddDays(-7 - (int)s.World.Term.StartDate.DayOfWeek)));
        var afterTerm = await PostAsync(s, Cancel(s.World.Term.EndDate.AddDays(14)));

        Assert.All(new[] { monday, friday, beforeTerm, afterTerm }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(ProblemCodes.exception_date_invalid, await CodeAsync(monday));
        Assert.Equal(ProblemCodes.exception_date_invalid, await CodeAsync(afterTerm));
    }

    [Fact]
    public async Task A_draft_slot_takes_no_exception_and_a_date_takes_only_one()
    {
        var draft = await SetUpAsync("TX3", publish: false);
        var refused = await PostAsync(draft, Cancel(draft.Date));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.timetable_not_published), (refused.StatusCode, await CodeAsync(refused)));

        var s = await SetUpAsync("TX4");
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(s, Cancel(s.Date))).StatusCode);
        var again = await PostAsync(s, Cancel(s.Date));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.exception_date_invalid), (again.StatusCode, await CodeAsync(again)));
    }

    [Fact]
    public async Task Substitute_rules()
    {
        var s = await SetUpAsync("TX5");
        var (_, free) = await AddCourseWithTeacherAsync(factory, s.World.Unit, s.World.Class); // a teacher of the school with no slot

        SaveTimetableExceptionRequest Sub(Guid? user) => new(s.Date, ExceptionKinds.Substitute, user, null, null);
        var self = await PostAsync(s, Sub(s.World.MathTeacher));
        var nobody = await PostAsync(s, Sub(null));
        var stranger = await PostAsync(s, Sub((await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(factory)).Id));
        var ok = await PostAsync(s, Sub(free));

        Assert.All(new[] { self, nobody, stranger }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(ProblemCodes.exception_substitute_invalid, await CodeAsync(self));
        Assert.Equal(ProblemCodes.exception_substitute_invalid, await CodeAsync(stranger));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(free, (await ok.Content.ReadFromJsonAsync<TimetableExceptionDto>(Web))!.SubstituteUserId);
    }

    [Fact]
    public async Task A_substitute_teaching_the_same_period_that_day_is_refused()
    {
        var s = await SetUpAsync("TX6");
        // The Science teacher teaches the second period on Sundays; the Math slot is at the first, so they are free there ...
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var other = new TimetableSlot(s.World.Class, s.World.Science.Id, s.World.Term.Id, s.World.ScienceTeacher, DayOfWeek.Sunday, s.World.Second.Id, null);
            other.Publish();
            db.Add(other);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Substitute, s.World.ScienceTeacher, null, null))).StatusCode);

        // ... but a second Math slot at the second period would clash once the Science teacher covers the first period too.
        var secondMath = await PublishedSlotAtSecondPeriodAsync(s);
        var clash = await SendAsync(factory, HttpMethod.Post, Routes.TimetableSlotExceptions(secondMath.Id), s.Admin,
            new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Substitute, s.World.ScienceTeacher, null, null));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.exception_substitute_invalid), (clash.StatusCode, await CodeAsync(clash)));
    }

    private async Task<TimetableSlot> PublishedSlotAtSecondPeriodAsync(Setup s)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = new TimetableSlot(s.World.Class, s.World.Math.Id, s.World.Term.Id, s.World.MathTeacher, DayOfWeek.Sunday, s.World.Second.Id, null);
        slot.Publish();
        db.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }

    [Fact]
    public async Task Moved_needs_another_period_of_the_unit_and_cancelled_needs_nothing()
    {
        var s = await SetUpAsync("TX7");

        var same = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Moved, null, s.World.First.Id, null));
        var missing = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Moved, null, null, null));
        var unknown = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Moved, null, Guid.NewGuid(), null));
        var badKind = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, "Teleported", null, null, null));
        var cancelled = await PostAsync(s, Cancel(s.Date));

        Assert.All(new[] { same, missing, unknown, badKind }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(HttpStatusCode.Created, cancelled.StatusCode);

        var list = await SendAsync(factory, HttpMethod.Get, Routes.TimetableSlotExceptions(s.Slot.Id), s.Admin);
        Assert.Equal([ExceptionKinds.Cancelled], (await list.Content.ReadFromJsonAsync<List<TimetableExceptionDto>>(Web))!.Select(e => e.Kind));
    }

    [Fact]
    public async Task Edit_keeps_the_date_and_delete_frees_it()
    {
        var s = await SetUpAsync("TX8");
        var created = (await (await PostAsync(s, Cancel(s.Date))).Content.ReadFromJsonAsync<TimetableExceptionDto>(Web))!;

        var moved = await SendAsync(factory, HttpMethod.Put, Routes.TimetableSlotException(s.Slot.Id, created.Id), s.Admin,
            new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Moved, null, s.World.Second.Id, null));
        var otherDate = await SendAsync(factory, HttpMethod.Put, Routes.TimetableSlotException(s.Slot.Id, created.Id), s.Admin, Cancel(s.Date.AddDays(7)));
        var deleted = await SendAsync(factory, HttpMethod.Delete, Routes.TimetableSlotException(s.Slot.Id, created.Id), s.Admin);
        var again = await PostAsync(s, Cancel(s.Date));

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(ExceptionKinds.Moved, (await moved.Content.ReadFromJsonAsync<TimetableExceptionDto>(Web))!.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, otherDate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task A_teacher_reads_but_cannot_write_and_another_school_gets_404()
    {
        var s = await SetUpAsync("TX9");
        var teacher = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Teacher, s.World.MathTeacher);
        var otherSchool = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(s, Cancel(s.Date), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Get, Routes.TimetableSlotExceptions(s.Slot.Id), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(s, Cancel(s.Date), otherSchool)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.TimetableSlotExceptions(s.Slot.Id), otherSchool)).StatusCode);
    }

    [Fact]
    public async Task A_substitute_assigned_in_a_sibling_school_still_sees_the_lesson_on_that_date()
    {
        var s = await SetUpAsync("TX11");
        var other = await NewWorldAsync(factory, s.Tree.School2);
        var covering = other.MathTeacher; // a teacher of the sibling school
        var admin = s.Admin; // client root admin: sees both schools
        var created = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Substitute, covering, null, null), admin);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var inSchool2 = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School2, BuiltInRoles.Teacher, covering);

        var response = await SendAsync(factory, HttpMethod.Get, Routes.MyTimetable(s.Date.AddDays(-7), s.Date.AddDays(14)), inSchool2);
        var view = (await response.Content.ReadFromJsonAsync<TimetableViewDto>(Web))!;

        var card = Assert.Single(view.Cards, c => c.SlotId == s.Slot.Id); // the lesson of the other school, on the date only
        Assert.Equal((s.Date, ExceptionKinds.Substitute), (card.Date, card.ExceptionKind));
        await using var db = factory.CreateDbContext(TestCurrentContext.For(s.Tree.Org.Id, s.Tree.School2.Path)); // the scope of the covering teacher excludes the slot
        Assert.False(await db.Set<TimetableSlot>().AnyAsync(x => x.Id == s.Slot.Id));
        Assert.True(await new SubstituteRelationship(db).IsSubstituteOnAsync(s.Tree.Org.Id, s.Slot.Id, covering, s.Date, CancellationToken.None));
        Assert.False(await new SubstituteRelationship(db).IsSubstituteOnAsync(s.Tree.Org.Id, s.Slot.Id, covering, s.Date.AddDays(7), CancellationToken.None));
    }

    [Fact]
    public async Task A_teacher_busy_in_a_sibling_school_cannot_be_the_substitute()
    {
        var s = await SetUpAsync("TX12");
        var other = await NewWorldAsync(factory, s.Tree.School2);
        var school1Admin = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var (_, busy) = await AddCourseWithTeacherAsync(factory, s.World.Unit, s.World.Class); // teaches in School1 too, so a School1 admin may pick them
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // The same period (a period defined above both schools has one id) on the same Sunday in the sibling school.
            var clash = new TimetableSlot(other.Class, other.Math.Id, s.World.Term.Id, busy, DayOfWeek.Sunday, s.World.First.Id, null); // one shared term (a year defined above both schools)
            clash.Publish();
            db.Add(clash);
            await db.SaveChangesAsync();
        }

        var refused = await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Substitute, busy, null, null), school1Admin);

        Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, refused.StatusCode + " " + await refused.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.exception_substitute_invalid), (refused.StatusCode, await CodeAsync(refused)));
    }

    [Fact]
    public async Task A_past_date_takes_no_exception()
    {
        var s = await SetUpAsync("TX13");
        var past = new DateOnly(2020, 1, 5); // a Sunday long gone; the slot weekday matches but the lesson already took place

        var refused = await PostAsync(s, Cancel(past));

        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.exception_date_invalid), (refused.StatusCode, await CodeAsync(refused)));
    }

    [Fact]
    public async Task Substitute_rule_holds_only_on_the_exception_date_and_for_that_slot()
    {
        var s = await SetUpAsync("TX10");
        var (_, free) = await AddCourseWithTeacherAsync(factory, s.World.Unit, s.World.Class);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(s, new SaveTimetableExceptionRequest(s.Date, ExceptionKinds.Substitute, free, null, null))).StatusCode);
        var otherSlot = await PublishedSlotAtSecondPeriodAsync(s);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var relationship = new SubstituteRelationship(db);

        Assert.True(await relationship.IsSubstituteOnAsync(s.World.Unit.OrganizationId, s.Slot.Id, free, s.Date, CancellationToken.None));
        Assert.False(await relationship.IsSubstituteOnAsync(s.World.Unit.OrganizationId, s.Slot.Id, free, s.Date.AddDays(7), CancellationToken.None)); // another date
        Assert.False(await relationship.IsSubstituteOnAsync(s.World.Unit.OrganizationId, s.Slot.Id, s.World.ScienceTeacher, s.Date, CancellationToken.None)); // another user
        Assert.False(await relationship.IsSubstituteOnAsync(s.World.Unit.OrganizationId, otherSlot.Id, free, s.Date, CancellationToken.None)); // another slot of the class
    }
}
