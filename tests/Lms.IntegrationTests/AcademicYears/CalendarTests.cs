using System.Net;
using System.Net.Http.Json;
using Lms.Domain.AcademicYears;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.AcademicYears;

[Collection(ApiCollection.Name)]
public sealed class CalendarTests(LmsApiFactory factory)
{
    private static readonly DateOnly Y1 = new(2033, 9, 1);

    private static SaveCalendarEventRequest Event(Guid yearId, Guid unitId, string title, DateOnly start, DateOnly end, string kind = "Holiday") =>
        new(yearId, unitId, "عطلة " + title, "Holiday " + title, kind, start, end);

    private async Task<IReadOnlyList<CalendarEventDto>> CalendarAsync(Lms.Domain.Access.RoleAssignment who, Guid yearId, Guid unitId)
    {
        var response = await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/{yearId}/calendar?orgUnitId={unitId}", who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<CalendarEventDto>>(Web))!;
    }

    [Fact]
    public async Task Root_holiday_applies_to_school_and_is_inherited()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));
        var created = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "National", Y1.AddDays(30), Y1.AddDays(31)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var atSchool = await CalendarAsync(school, year.Id, tree.School1.Id);

        var e = Assert.Single(atSchool);
        Assert.True(e.IsInherited);
        Assert.Equal(tree.Root.Id, e.OrgUnitId);
        Assert.Equal("عطلة National", e.TitleAr);
        Assert.False((await CalendarAsync(admin, year.Id, tree.Root.Id))[0].IsInherited);
    }

    [Fact]
    public async Task School_event_does_not_appear_for_sibling_school()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));
        (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.School1.Id, "S1", Y1.AddDays(5), Y1.AddDays(5), "Event"))).EnsureSuccessStatusCode();

        Assert.Single(await CalendarAsync(admin, year.Id, tree.School1.Id));
        Assert.Empty(await CalendarAsync(admin, year.Id, tree.School2.Id));
        Assert.Empty(await CalendarAsync(admin, year.Id, tree.Root.Id));
    }

    [Fact]
    public async Task Event_outside_year_is_409_and_bad_input_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));

        var outside = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "X", Y1.AddDays(-1), Y1.AddDays(2)));
        var badKind = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "X", Y1, Y1.AddDays(1), "Party"));
        var reversed = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "X", Y1.AddDays(3), Y1.AddDays(1)));

        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Contains("event_outside_year", await outside.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
    }

    [Fact]
    public async Task IsNonWorkingDay_true_only_for_holidays_at_unit_or_ancestors()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));
        (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "H", Y1.AddDays(10), Y1.AddDays(11)))).EnsureSuccessStatusCode();
        (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "Ev", Y1.AddDays(20), Y1.AddDays(20), "Event"))).EnsureSuccessStatusCode();
        (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.School1.Id, "S1", Y1.AddDays(30), Y1.AddDays(30)))).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<IAcademicCalendar>();

        Assert.True(await calendar.IsNonWorkingDayAsync(tree.School1.Path, Y1.AddDays(11), default));
        Assert.False(await calendar.IsNonWorkingDayAsync(tree.School1.Path, Y1.AddDays(12), default));
        Assert.False(await calendar.IsNonWorkingDayAsync(tree.School1.Path, Y1.AddDays(20), default)); // an Event, not a Holiday
        Assert.True(await calendar.IsNonWorkingDayAsync(tree.School1.Path, Y1.AddDays(30), default));
        Assert.False(await calendar.IsNonWorkingDayAsync(tree.School2.Path, Y1.AddDays(30), default)); // sibling's holiday
    }

    [Fact]
    public async Task Closed_year_calendar_is_read_only()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL5");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));
        var ev = (await (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "H", Y1.AddDays(2), Y1.AddDays(3)))).Content.ReadFromJsonAsync<CalendarEventDto>(Web))!;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var y = await db.Set<AcademicYear>().SingleAsync(x => x.Id == year.Id);
            y.Close();
            await db.SaveChangesAsync();
        }

        var add = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.Root.Id, "N", Y1.AddDays(5), Y1.AddDays(6)));
        var put = await SendAsync(factory, HttpMethod.Put, $"/api/calendar-events/{ev.Id}", admin, Event(year.Id, tree.Root.Id, "M", Y1.AddDays(2), Y1.AddDays(3)));
        var del = await SendAsync(factory, HttpMethod.Delete, $"/api/calendar-events/{ev.Id}", admin);

        foreach (var r in new[] { add, put, del })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("year_closed", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Update_works_and_deleting_a_year_removes_its_events()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL6");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));
        var ev = (await (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", admin, Event(year.Id, tree.School1.Id, "A", Y1.AddDays(2), Y1.AddDays(3)))).Content.ReadFromJsonAsync<CalendarEventDto>(Web))!;

        var put = await SendAsync(factory, HttpMethod.Put, $"/api/calendar-events/{ev.Id}", admin, Event(year.Id, tree.School1.Id, "B", Y1.AddDays(4), Y1.AddDays(5), "Event"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = (await put.Content.ReadFromJsonAsync<CalendarEventDto>(Web))!;
        Assert.Equal("Event", updated.Kind);
        Assert.Equal("Holiday B", updated.TitleEn);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{year.Id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/calendar-events/{ev.Id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_write_the_calendar()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CL7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "C", Y1, Y1.AddMonths(10));

        var r = await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", teacher, Event(year.Id, tree.Root.Id, "T", Y1.AddDays(2), Y1.AddDays(3)));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(await CalendarAsync(teacher, year.Id, tree.Root.Id));
    }
}
