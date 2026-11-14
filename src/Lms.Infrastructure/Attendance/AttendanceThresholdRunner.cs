using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Attendance;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Calendar;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Attendance;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance;

/// <summary>A student who crossed the threshold in this run (the counselors are told once, by the job).</summary>
public sealed record NewAttendanceAlert(Guid OrganizationId, string OrgUnitPath, Guid StudentId, string NameAr, string NameEn, int Absences, int WindowDays);

/// <summary>
/// The absence threshold check (FR-EXC-04, D61, M5 Task 20): per client, in the context of one client and one of its root units (never the platform context for
/// writes). For each student with Absent or Late records in the window of their unit (<c>Attendance.ThresholdWindowDays</c>, counted back from the client-local today) the
/// score is absences + lates x <c>Attendance.LateWeightPercent</c> / 100 (an Excused record never counts). At <c>Attendance.AbsentThreshold</c> one alert opens; it
/// is not repeated until the score has dropped below the threshold (the alert is cleared) and crosses again. Idempotent through the unique active key.
/// </summary>
public sealed class AttendanceThresholdRunner(IConfiguration configuration, IClock clock, ILogger<AttendanceThresholdRunner> logger)
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

    public async Task<IReadOnlyList<NewAttendanceAlert>> RunAllAsync(CancellationToken ct)
    {
        List<Guid> organizations;
        await using (var platform = LmsDbContextFactory.Create(ConnectionString, new PlatformReadContext()))
        {
            organizations = await platform.Set<Organization>().Select(o => o.Id).ToListAsync(ct); // clients are platform-level data
        }

        var all = new List<NewAttendanceAlert>();
        foreach (var organizationId in organizations)
        {
            try
            {
                all.AddRange(await RunForOrganizationAsync(organizationId, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The attendance threshold check failed for client {OrganizationId}", organizationId); // the next run retries it
            }
        }

        return all;
    }

    public async Task<IReadOnlyList<NewAttendanceAlert>> RunForOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        List<string> roots;
        await using (var platform = LmsDbContextFactory.Create(ConnectionString, new PlatformReadContext()))
        {
            roots = await platform.Set<OrgUnit>().Where(u => u.OrganizationId == organizationId && u.ParentId == null).Select(u => u.Path).ToListAsync(ct);
        }

        var created = new List<NewAttendanceAlert>();
        foreach (var root in roots)
        {
            var context = new ClientContext(organizationId, root);
            await using var db = LmsDbContextFactory.Create(ConnectionString, context, clock);
            created.AddRange(await CheckAsync(db, context, organizationId, ct));
        }

        return created;
    }

    private async Task<IReadOnlyList<NewAttendanceAlert>> CheckAsync(LmsDbContext db, ICurrentContext context, Guid organizationId, CancellationToken ct)
    {
        var settings = new SettingsResolver(db, context);
        var calendar = new ClientCalendar(db, settings, new AcademicCalendar(db, context, new AcademicYearResolver(db, context)), clock);
        var today = await calendar.LocalTodayAsync(organizationId, ct);
        var now = clock.UtcNow;

        var active = await db.Set<AttendanceAlert>().Where(a => a.ActiveKey != null).ToListAsync(ct);
        var students = await db.Set<StudentProfile>().Where(s => !s.IsDeleted).ToDictionaryAsync(s => s.Id, ct);
        var paths = students.Values.Select(s => s.OrgUnitPath).Distinct().ToList();
        var rules = new Dictionary<string, (int Window, int Threshold, int LateWeight)>();
        foreach (var path in paths)
        {
            rules[path] = ((await settings.ResolveAsync(SettingKeys.AttendanceThresholdWindowDays, path, ct)).Value,
                (await settings.ResolveAsync(SettingKeys.AttendanceAbsentThreshold, path, ct)).Value,
                (await settings.ResolveAsync(SettingKeys.AttendanceLateWeightPercent, path, ct)).Value);
        }

        var widest = rules.Count == 0 ? 0 : rules.Values.Max(r => r.Window);
        var marks = widest == 0 ? [] : await (from r in db.Set<AttendanceRecord>()
                                              join s in db.Set<AttendanceSession>() on r.AttendanceSessionId equals s.Id
                                              where s.State == AttendanceSession.TakenState && s.Date > today.AddDays(-widest) && s.Date <= today
                                                  && (r.Status == AttendanceStatuses.Absent || r.Status == AttendanceStatuses.Late)
                                              select new { r.StudentProfileId, s.Date, r.Status }).ToListAsync(ct);
        var byStudent = marks.GroupBy(m => m.StudentProfileId).ToDictionary(g => g.Key, g => g.ToList());

        var created = new List<NewAttendanceAlert>();
        foreach (var (studentId, student) in students)
        {
            var (window, threshold, lateWeight) = rules[student.OrgUnitPath];
            var inWindow = byStudent.GetValueOrDefault(studentId)?.Where(m => m.Date > today.AddDays(-window)).ToList() ?? [];
            var absences = inWindow.Count(m => m.Status == AttendanceStatuses.Absent);
            var lates = inWindow.Count(m => m.Status == AttendanceStatuses.Late);
            var crossed = ThresholdCalculator.Crossed(ThresholdCalculator.Score(absences, lates, lateWeight), threshold);
            var open = active.FirstOrDefault(a => a.StudentProfileId == studentId);
            if (crossed && open is null)
            {
                db.Add(new AttendanceAlert(student, absences, lates, window, ThresholdCalculator.Score(absences, lates, lateWeight), threshold, now));
                created.Add(new NewAttendanceAlert(organizationId, student.OrgUnitPath, studentId, student.NameAr, student.NameEn, absences, window));
            }
            else if (!crossed && open is not null)
            {
                open.Clear(now);
            }
        }

        await db.SaveChangesAsync(ct);
        return created;
    }
}
