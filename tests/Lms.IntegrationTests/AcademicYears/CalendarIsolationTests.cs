using System.Net;
using System.Net.Http.Json;
using Lms.Domain.AcademicYears;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.AcademicYears;

[Collection(ApiCollection.Name)]
public sealed class CalendarIsolationTests(LmsApiFactory factory)
{
    private static readonly DateOnly Y1 = new(2034, 9, 1);

    [Fact]
    public async Task Client_B_events_invisible()
    {
        var a = await AccessTestData.SeedAsync(factory, "CI1A");
        var b = await AccessTestData.SeedAsync(factory, "CI1B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var adminB = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school1Admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, adminA, a.Root, "I", Y1, Y1.AddMonths(10));
        var ev = (await (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", adminA,
            new SaveCalendarEventRequest(year.Id, a.School2.Id, "عطلة", "E", "Holiday", Y1.AddDays(2), Y1.AddDays(3)))).Content.ReadFromJsonAsync<CalendarEventDto>(Web))!;

        // Client B cannot read, write or touch A's year or events, with A's ids.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/{year.Id}/calendar?orgUnitId={b.Root.Id}", adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, "/api/calendar-events", adminB,
            new SaveCalendarEventRequest(year.Id, b.Root.Id, "عطلة", "E", "Holiday", Y1.AddDays(2), Y1.AddDays(3)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/calendar-events/{ev.Id}", adminB)).StatusCode);

        // School 1 admin cannot reach School 2's event or unit.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/calendar-events/{ev.Id}", school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/{year.Id}/calendar?orgUnitId={a.School2.Id}", school1Admin)).StatusCode);

        // The table's query filter hides the row outside the caller's scope.
        await using var dbS1 = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.False(await dbS1.Set<CalendarEvent>().AnyAsync(e => e.Id == ev.Id));
        await using var dbB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.False(await dbB.Set<CalendarEvent>().AnyAsync(e => e.Id == ev.Id));
    }
}
