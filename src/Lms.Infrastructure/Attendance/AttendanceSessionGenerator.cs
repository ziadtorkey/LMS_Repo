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
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance;

/// <summary>
/// Materialises attendance sessions from published timetable slots (FR-TT-06, FR-ATT-01, M5 Task 16, plan site 4). The clients are listed in the platform
/// context (reads only); every <b>write</b> happens in the context of one client and one of its root units, never in the platform context, so the global
/// filters apply to every query and write. For each published slot of a class that is still active it creates the sessions of the next
/// <c>Attendance.GenerateDaysAhead</c> days, from the client-local today forward (never in the past): only on the slot weekday, inside its term, on a working
/// day that is not a holiday, with the dated exceptions applied (cancelled: none; moved: the new period). Idempotent through the unique (slot, date) key.
/// </summary>
public sealed class AttendanceSessionGenerator(IConfiguration configuration, IClock clock, ILogger<AttendanceSessionGenerator> logger)
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
            try
            {
                created += await GenerateForOrganizationAsync(organizationId, null, null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One client failing must not stop the others; the next run retries it (generation is idempotent).
                logger.LogError(ex, "Attendance generation failed for client {OrganizationId}", organizationId);
            }
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

        var pending = new List<(ClassCourse ClassCourse, DateOnly Date, Guid SlotId, Guid? PeriodId)>();
        var workingDays = new Dictionary<(Guid, DateOnly), bool>();
        var holidays = new Dictionary<(Guid, DateOnly), bool>();
        foreach (var slot in slots)
        {
            if (!activeClasses.Contains(slot.ClassId) || !terms.TryGetValue(slot.TermId, out var term) || !classCourses.TryGetValue(slot.ClassCourseId, out var classCourse))
            {
                continue;
            }

            var daysAhead = (await settings.ResolveAsync(SettingKeys.AttendanceGenerateDaysAhead, slot.OrgUnitPath, ct)).Value;
            for (var date = today; date <= today.AddDays(daysAhead); date = date.AddDays(1))
            {
                if (date.DayOfWeek != slot.Day || date < term.StartDate || date > term.EndDate || existing.Contains((slot.Id, date)))
                {
                    continue;
                }

                // The calendar answers per unit and date: ask once and remember (a school has many slots on the same days).
                if (!workingDays.TryGetValue((slot.OrgUnitId, date), out var working))
                {
                    workingDays[(slot.OrgUnitId, date)] = working = await calendar.IsWorkingDayAsync(slot.OrgUnitId, date, ct);
                }

                if (!working)
                {
                    continue;
                }

                if (!holidays.TryGetValue((slot.OrgUnitId, date), out var holiday))
                {
                    holidays[(slot.OrgUnitId, date)] = holiday = await calendar.IsHolidayAsync(slot.OrgUnitId, date, ct);
                }

                if (holiday)
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

                pending.Add((classCourse, date, slot.Id, periodId));
                existing.Add((slot.Id, date));
            }
        }

        return await SaveAsync(db, pending, ct);
    }

    /// <summary>
    /// Saves the sessions in one batch. Two runs can overlap (the daily job and the run after a publish): when the unique (slot, date) key refuses the batch
    /// because the other run got there first, the rows that are still missing are inserted one by one and the duplicates are ignored, so neither run fails.
    /// </summary>
    private static async Task<int> SaveAsync(LmsDbContext db, List<(ClassCourse ClassCourse, DateOnly Date, Guid SlotId, Guid? PeriodId)> pending, CancellationToken ct)
    {
        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (var p in pending)
        {
            db.Add(new AttendanceSession(p.ClassCourse, p.Date, p.SlotId, p.PeriodId, AttendanceSession.TimetableSource));
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return pending.Count;
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
        }

        var created = 0;
        foreach (var p in pending)
        {
            db.Add(new AttendanceSession(p.ClassCourse, p.Date, p.SlotId, p.PeriodId, AttendanceSession.TimetableSource));
            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (IsDuplicate(ex))
            {
                db.ChangeTracker.Clear(); // the other run created this one
            }
        }

        return created;
    }

    /// <summary>True when the database refused the row because of a unique key (MySQL error 1062).</summary>
    public static bool IsDuplicate(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
