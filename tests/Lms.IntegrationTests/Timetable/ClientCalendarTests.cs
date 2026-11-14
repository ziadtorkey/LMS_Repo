using Lms.Domain.AcademicYears;
using Lms.Domain.Calendar;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.Infrastructure.Configuration;
using Lms.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Timetable;

[Collection(ApiCollection.Name)]
public sealed class ClientCalendarTests(LmsApiFactory factory)
{
    // Sunday 2027-01-03 ... Thursday 2027-01-07, Sunday 2027-01-10 (the default working week is Sunday to Thursday).
    private static readonly DateOnly Thursday = new(2027, 1, 7);

    private static async Task SetAsync<T>(EngineScope scope, SettingKey<T> key, Guid unitId, T value) =>
        await scope.Settings.SetAsync(key, unitId, value, false);

    [Fact]
    public async Task Missing_client_time_zone_defaults_to_asia_riyadh()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC1");
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();

        Assert.Equal("Asia/Riyadh", (await calendar.GetTimeZoneAsync(tree.Org.Id, default)).Id);
        // 21:30 UTC is already the next day in Riyadh (UTC+3).
        Assert.Equal(new DateOnly(2027, 1, 6), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 1, 5, 21, 30, 0, DateTimeKind.Utc), default));
        Assert.Equal(new DateOnly(2027, 1, 5), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 1, 5, 20, 59, 0, DateTimeKind.Utc), default));
    }

    [Fact]
    public async Task Local_today_uses_the_client_time_zone()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC2");
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();
        await SetAsync(scope, SettingKeys.OrgTimeZone, tree.Root.Id, "Pacific/Kiritimati"); // UTC+14

        var zone = await calendar.GetTimeZoneAsync(tree.Org.Id, default);
        var expected = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));

        Assert.Equal("Pacific/Kiritimati", zone.Id);
        Assert.Equal(expected, await calendar.LocalTodayAsync(tree.Org.Id, default));
        // A school cannot override the client-level zone by resolving from below: the root decides.
        Assert.Equal(new DateOnly(2027, 1, 6), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 1, 5, 12, 0, 0, DateTimeKind.Utc), default));
    }

    [Fact]
    public async Task An_unknown_time_zone_falls_back_to_the_default()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC3");
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();
        await SetAsync(scope, SettingKeys.OrgTimeZone, tree.Root.Id, "Mars/Olympus_Mons");

        Assert.Equal("Asia/Riyadh", (await calendar.GetTimeZoneAsync(tree.Org.Id, default)).Id);
    }

    [Fact]
    public async Task Dst_change_keeps_local_dates()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC4");
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();
        await SetAsync(scope, SettingKeys.OrgTimeZone, tree.Root.Id, "Europe/London");

        // London switches to summer time at 01:00 UTC on 2027-03-28: before it local = UTC, after it local = UTC+1.
        Assert.Equal(new DateOnly(2027, 3, 27), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 3, 27, 23, 30, 0, DateTimeKind.Utc), default));
        Assert.Equal(new DateOnly(2027, 3, 28), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 3, 28, 0, 30, 0, DateTimeKind.Utc), default));
        Assert.Equal(new DateOnly(2027, 3, 29), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 3, 28, 23, 30, 0, DateTimeKind.Utc), default));
        Assert.Equal(new DateOnly(2027, 3, 29), await calendar.LocalDateAsync(tree.Org.Id, new DateTime(2027, 3, 29, 0, 30, 0, DateTimeKind.Utc), default));
    }

    [Fact]
    public async Task Working_days_come_from_the_unit_setting()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC5");
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();

        Assert.True(await calendar.IsWorkingDayAsync(tree.School1.Id, Thursday, default));
        Assert.False(await calendar.IsWorkingDayAsync(tree.School1.Id, Thursday.AddDays(1), default)); // Friday
        Assert.False(await calendar.IsWorkingDayAsync(tree.School1.Id, Thursday.AddDays(2), default)); // Saturday

        await SetAsync<IReadOnlyList<string>>(scope, SettingKeys.TimetableWorkingDays, tree.School1.Id, ["Mon", "Tue"]);

        Assert.False(await calendar.IsWorkingDayAsync(tree.School1.Id, Thursday, default));
        Assert.True(await calendar.IsWorkingDayAsync(tree.School1.Id, Thursday.AddDays(4), default)); // Monday 2027-01-11
        Assert.True(await calendar.IsWorkingDayAsync(tree.School2.Id, Thursday, default)); // the sibling keeps the default
    }

    [Fact]
    public async Task Holidays_come_from_the_calendar()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC6");
        await SeedHolidayAsync(tree, tree.School1, new DateOnly(2027, 1, 10));
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();

        Assert.True(await calendar.IsHolidayAsync(tree.School1.Id, new DateOnly(2027, 1, 10), default));
        Assert.False(await calendar.IsHolidayAsync(tree.School1.Id, new DateOnly(2027, 1, 11), default));
        Assert.False(await calendar.IsHolidayAsync(tree.School2.Id, new DateOnly(2027, 1, 10), default)); // a sibling's holiday
    }

    [Fact]
    public async Task Add_school_days_skips_weekends_and_holidays()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC7");
        await SeedHolidayAsync(tree, tree.Root, new DateOnly(2027, 1, 11)); // Monday
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();

        Assert.Equal(Thursday, await calendar.AddSchoolDaysAsync(tree.School1.Id, Thursday, 0, default));
        Assert.Equal(new DateOnly(2027, 1, 10), await calendar.AddSchoolDaysAsync(tree.School1.Id, Thursday, 1, default)); // Fri, Sat skipped
        Assert.Equal(new DateOnly(2027, 1, 12), await calendar.AddSchoolDaysAsync(tree.School1.Id, Thursday, 2, default)); // Monday is a holiday
        Assert.Equal(new DateOnly(2027, 1, 7), await calendar.AddSchoolDaysAsync(tree.School1.Id, new DateOnly(2027, 1, 10), -1, default)); // back over the weekend
        Assert.Equal(new DateOnly(2027, 1, 10), await calendar.AddSchoolDaysAsync(tree.School1.Id, new DateOnly(2027, 1, 12), -1, default)); // back over the holiday
    }

    [Fact]
    public async Task School_days_between_counts_after_the_first_date_up_to_the_second()
    {
        var tree = await AccessTestData.SeedAsync(factory, "CC8");
        await SeedHolidayAsync(tree, tree.Root, new DateOnly(2027, 1, 11)); // Monday
        using var scope = new EngineScope(factory.Services);
        var calendar = scope.Services.GetRequiredService<IClientCalendar>();

        Assert.Equal(0, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday, default));
        Assert.Equal(0, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday.AddDays(-1), default));
        Assert.Equal(0, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday.AddDays(2), default)); // Fri, Sat
        Assert.Equal(1, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday.AddDays(3), default)); // Sunday
        Assert.Equal(1, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday.AddDays(4), default)); // Monday is a holiday
        Assert.Equal(2, await calendar.SchoolDaysBetweenAsync(tree.School1.Id, Thursday, Thursday.AddDays(5), default)); // Tuesday
    }

    private async Task SeedHolidayAsync(AccessTree tree, Lms.Domain.Organizations.OrgUnit unit, DateOnly date)
    {
        var year = await SeedYearAsync(factory, tree.Root, new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), AcademicYearStatus.Active);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Set<CalendarEvent>().Add(new CalendarEvent(year, unit, "عطلة", "Holiday", CalendarEventKind.Holiday, date, date));
        await db.SaveChangesAsync();
    }
}
