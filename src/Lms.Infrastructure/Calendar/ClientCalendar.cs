using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Calendar;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Calendar;

internal sealed class ClientCalendar(LmsDbContext db, ISettingsResolver settings, IAcademicCalendar holidays, IClock clock) : IClientCalendar
{
    private const int LookAheadDays = 366;

    public async Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // ISOLATION-BYPASS: the client root sits above the caller's subtree scope (a school admin cannot see it); only its path is read,
        // for the given client, to resolve the client-level Org.TimeZone setting.
        var rootPath = await db.Set<OrgUnit>().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && u.ParentId == null)
            .Select(u => u.Path).FirstOrDefaultAsync(cancellationToken);
        var id = rootPath is null
            ? SettingKeys.OrgTimeZone.Default
            : (await settings.ResolveAsync(SettingKeys.OrgTimeZone, rootPath, cancellationToken)).Value;
        return Find(id) ?? Find(SettingKeys.OrgTimeZone.Default) ?? TimeZoneInfo.Utc;
    }

    public async Task<DateOnly> LocalTodayAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await LocalDateAsync(organizationId, clock.UtcNow, cancellationToken);

    public async Task<DateOnly> LocalDateAsync(Guid organizationId, DateTime instantUtc, CancellationToken cancellationToken)
    {
        var zone = await GetTimeZoneAsync(organizationId, cancellationToken);
        var utc = DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }

    public async Task<bool> IsWorkingDayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken)
    {
        var days = await WorkingDaysAsync(orgUnitId, cancellationToken);
        return days.Contains(WorkingDayName(date.DayOfWeek));
    }

    public async Task<bool> IsHolidayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken)
    {
        var unit = await FindUnitAsync(orgUnitId, cancellationToken);
        return await holidays.IsNonWorkingDayAsync(unit.Path, date, cancellationToken);
    }

    public async Task<DateOnly> AddSchoolDaysAsync(Guid orgUnitId, DateOnly from, int schoolDays, CancellationToken cancellationToken)
    {
        var unit = await FindUnitAsync(orgUnitId, cancellationToken);
        var working = await WorkingDaysAsync(orgUnitId, cancellationToken);
        if (schoolDays != 0 && working.Count == 0)
        {
            throw new InvalidOperationException("The unit has no working day to count school days on.");
        }

        var step = schoolDays < 0 ? -1 : 1;
        var remaining = Math.Abs(schoolDays);
        var date = from;
        var scanned = 0;
        while (remaining > 0)
        {
            if (++scanned > LookAheadDays * Math.Max(1, Math.Abs(schoolDays)))
            {
                throw new InvalidOperationException("No school day found within the look-ahead limit.");
            }

            date = date.AddDays(step);
            if (working.Contains(WorkingDayName(date.DayOfWeek)) && !await holidays.IsNonWorkingDayAsync(unit.Path, date, cancellationToken))
            {
                remaining--;
            }
        }

        return date;
    }

    private async Task<IReadOnlyList<string>> WorkingDaysAsync(Guid orgUnitId, CancellationToken cancellationToken)
    {
        var unit = await FindUnitAsync(orgUnitId, cancellationToken);
        return (await settings.ResolveAsync(SettingKeys.TimetableWorkingDays, unit.Path, cancellationToken)).Value;
    }

    private async Task<OrgUnit> FindUnitAsync(Guid orgUnitId, CancellationToken cancellationToken) =>
        await db.Set<OrgUnit>().SingleOrDefaultAsync(u => u.Id == orgUnitId, cancellationToken)
        ?? throw new InvalidOperationException("The org unit does not exist for this caller.");

    private static string WorkingDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => WorkingDayNames.Sunday,
        DayOfWeek.Monday => WorkingDayNames.Monday,
        DayOfWeek.Tuesday => WorkingDayNames.Tuesday,
        DayOfWeek.Wednesday => WorkingDayNames.Wednesday,
        DayOfWeek.Thursday => WorkingDayNames.Thursday,
        DayOfWeek.Friday => WorkingDayNames.Friday,
        _ => WorkingDayNames.Saturday,
    };

    private static TimeZoneInfo? Find(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}
