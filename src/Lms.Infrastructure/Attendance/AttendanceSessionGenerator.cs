using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Calendar;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Lms.Infrastructure.Attendance;

/// <summary>
/// Materialises attendance sessions from published timetable slots (FR-TT-06, FR-ATT-01, M5 Task 16, plan site 4). The clients are listed in the platform
/// context (reads only); every <b>write</b> happens in the context of one client and one of its root units, never in the platform context, so the global
/// filters apply to every query and write. For each published slot of a class that is still active it creates the sessions of the next
/// <c>Attendance.GenerateDaysAhead</c> days, from the client-local today forward (never in the past): only on the slot weekday, inside its term, on a working
/// day that is not a holiday, with the dated exceptions applied (cancelled: none; moved: the new period). Idempotent through the unique (slot, date) key.
/// </summary>
public sealed class AttendanceSessionGenerator(IConfiguration configuration, IClock clock)
{
    private sealed class ClientContext(Guid organizationId, string scopePath) : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => organizationId;
        public Guid? AssignmentId => null;
        public string? ScopePath => scopePath;
        public bool IsPlatformAdmin => false;
    }

    private sealed class PlatformReadContext : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? AssignmentId => null;
        public string? ScopePath => null;
        public bool IsPlatformAdmin => true;
    }

    private string ConnectionString => configuration.GetConnectionString("Lms")!;

    /// <summary>Every client (the daily job).</summary>
    public async Task<int> GenerateAllAsync(CancellationToken ct)
    {
        List<Guid> organizations;
        await using (var platform = LmsDbContextFactory.Create(ConnectionString, new PlatformReadContext()))
        {
            organizations = await platform.Set<Organization>().Select(o => o.Id).ToListAsync(ct); // clients are platform-level data
        }

        var created = 0;
        foreach (var organizationId in organizations)
        {
            created += await GenerateForOrganizationAsync(organizationId, null, null, ct);
        }

        return created;
    }

    /// <summary>One client, optionally one class and term (after a timetable publish).</summary>
    public async Task<int> GenerateForOrganizationAsync(Guid organizationId, Guid? classId, Guid? termId, CancellationToken ct)
    {
        List<string> roots;
        await using (var platform = LmsDbContextFactory.Create(ConnectionString, new PlatformReadContext()))
        {
            roots = await platform.Set<OrgUnit>().Where(u => u.OrganizationId == organizationId && u.ParentId == null).Select(u => u.Path).ToListAsync(ct);
        }

        var created = 0;
        foreach (var root in roots)
        {
            var context = new ClientContext(organizationId, root);
            await using var db = LmsDbContextFactory.Create(ConnectionString, context, clock);
            created += await GenerateInAsync(db, context, organizationId, classId, termId, ct);
        }

        return created;
    }

    private async Task<int> GenerateInAsync(LmsDbContext db, ICurrentContext context, Guid organizationId, Guid? classId, Guid? termId, CancellationToken ct)
    {
        var settings = new SettingsResolver(db, context);
        var calendar = new ClientCalendar(db, settings, new AcademicCalendar(db, context, new AcademicYearResolver(db, context)), clock);
        var today = await calendar.LocalTodayAsync(organizationId, ct);

        var slots = await db.Set<TimetableSlot>()
            .Where(s => s.State == TimetableSlot.PublishedState && (classId == null || s.ClassId == classId) && (termId == null || s.TermId == termId))
            .ToListAsync(ct);
        if (slots.Count == 0)
        {
            return 0;
        }

        var classIds = slots.Select(s => s.ClassId).Distinct().ToList();
        var activeClasses = await db.Set<Class>().Where(c => classIds.Contains(c.Id) && c.Status == Class.ActiveStatus).Select(c => c.Id).ToListAsync(ct);
        var termIds = slots.Select(s => s.TermId).Distinct().ToList();
        var terms = await db.Set<Term>().Where(t => termIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var classCourseIds = slots.Select(s => s.ClassCourseId).Distinct().ToList();
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var slotIds = slots.Select(s => s.Id).ToList();
        var exceptions = (await db.Set<TimetableException>().Where(e => slotIds.Contains(e.SlotId) && e.Date >= today).ToListAsync(ct))
            .ToDictionary(e => (e.SlotId, e.Date));
        var existing = (await db.Set<AttendanceSession>().Where(a => a.SlotId != null && slotIds.Contains(a.SlotId.Value) && a.Date >= today)
                .Select(a => new { SlotId = a.SlotId!.Value, a.Date }).ToListAsync(ct))
            .Select(a => (a.SlotId, a.Date)).ToHashSet();

        var created = 0;
        foreach (var slot in slots)
        {
            if (!activeClasses.Contains(slot.ClassId) || !terms.TryGetValue(slot.TermId, out var term) || !classCourses.TryGetValue(slot.ClassCourseId, out var classCourse))
            {
                continue;
            }

            var daysAhead = (await settings.ResolveAsync(SettingKeys.AttendanceGenerateDaysAhead, slot.OrgUnitPath, ct)).Value;
            for (var date = today; date <= today.AddDays(daysAhead); date = date.AddDays(1))
            {
                if (date.DayOfWeek != slot.Day || date < term.StartDate || date > term.EndDate || existing.Contains((slot.Id, date))
                    || !await calendar.IsWorkingDayAsync(slot.OrgUnitId, date, ct) || await calendar.IsHolidayAsync(slot.OrgUnitId, date, ct))
                {
                    continue;
                }

                var periodId = slot.PeriodId;
                if (exceptions.TryGetValue((slot.Id, date), out var exception))
                {
                    if (exception.Kind == TimetableException.CancelledKind)
                    {
                        continue;
                    }

                    if (exception.Kind == TimetableException.MovedKind && exception.NewPeriodId is { } moved)
                    {
                        periodId = moved;
                    }
                }

                db.Add(new AttendanceSession(classCourse, date, slot.Id, periodId, AttendanceSession.TimetableSource));
                existing.Add((slot.Id, date));
                created++;
            }
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return created;
    }
}
