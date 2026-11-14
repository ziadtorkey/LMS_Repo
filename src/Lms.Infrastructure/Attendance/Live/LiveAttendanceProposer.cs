using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.LiveSessions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Calendar;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance.Live;

/// <summary>
/// Creates the attendance proposal of an ended live session (FR-TT-06, FR-ATT-01, M5 Task 18, plan site 6, L11). It runs without a signed-in user, so every read and
/// write happens in the context of one client and one of its root units (never the platform context, so the global filters apply); the clients are listed in a
/// platform context for reads only. A session is proposed only when its participants were imported; only <c>Matched</c> students are marked from the rows, the rest
/// of the roster is proposed Absent, and guests are never attached to a student. One proposal per live session through the unique key, so the job, a retry and the
/// reconcile sweep are all safe to run twice.
/// </summary>
public sealed class LiveAttendanceProposer(IConfiguration configuration, IClock clock, ILogger<LiveAttendanceProposer> logger)
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

    /// <summary>The job of one ended live session. The client comes from the payload and is re-checked: a session that is not in that client is not found and nothing happens.</summary>
    public async Task<bool> ProposeAsync(Guid organizationId, Guid liveSessionId, CancellationToken ct)
    {
        foreach (var root in await RootsAsync(organizationId, ct))
        {
            var context = new ClientContext(organizationId, root);
            await using var db = LmsDbContextFactory.Create(ConnectionString, context, clock);
            if (await ProposeInAsync(db, context, organizationId, liveSessionId, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every client in its own context: creates the proposals the sink missed (an ended session without an attendance session). One client failing does not stop the others.</summary>
    public async Task<int> ReconcileAllAsync(CancellationToken ct)
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
                created += await ReconcileAsync(organizationId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Live attendance reconcile failed for client {OrganizationId}", organizationId);
            }
        }

        return created;
    }

    public async Task<int> ReconcileAsync(Guid organizationId, CancellationToken ct)
    {
        var created = 0;
        foreach (var root in await RootsAsync(organizationId, ct))
        {
            var context = new ClientContext(organizationId, root);
            await using var db = LmsDbContextFactory.Create(ConnectionString, context, clock);
            var proposed = db.Set<AttendanceSession>().Where(a => a.LiveSessionId != null).Select(a => a.LiveSessionId!.Value);
            var missing = await db.Set<LiveSession>()
                .Where(s => s.Status == LiveSessionRules.Ended && s.ParticipantsImportedAtUtc != null && !proposed.Contains(s.Id))
                .Select(s => s.Id).ToListAsync(ct);
            foreach (var id in missing)
            {
                if (await ProposeInAsync(db, context, organizationId, id, ct))
                {
                    created++;
                }
            }
        }

        return created;
    }

    private async Task<List<string>> RootsAsync(Guid organizationId, CancellationToken ct)
    {
        await using var platform = LmsDbContextFactory.Create(ConnectionString, new PlatformReadContext());
        return await platform.Set<OrgUnit>().Where(u => u.OrganizationId == organizationId && u.ParentId == null).Select(u => u.Path).ToListAsync(ct);
    }

    private async Task<bool> ProposeInAsync(LmsDbContext db, ICurrentContext context, Guid organizationId, Guid liveSessionId, CancellationToken ct)
    {
        var live = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == liveSessionId, ct);
        if (live is null || live.Status != LiveSessionRules.Ended || live.ParticipantsImportedAtUtc is null)
        {
            return false; // not ours, not ended, or the participants are not imported yet: nobody is proposed Absent
        }

        if (await db.Set<AttendanceSession>().AnyAsync(a => a.LiveSessionId == liveSessionId, ct))
        {
            return false;
        }

        var classCourse = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == live.ClassCourseId, ct);
        if (classCourse is null)
        {
            return false;
        }

        var settings = new SettingsResolver(db, context);
        var calendar = new ClientCalendar(db, settings, new AcademicCalendar(db, context, new AcademicYearResolver(db, context)), clock);
        var date = await calendar.LocalDateAsync(organizationId, live.StartsAtUtc, ct);
        var minPercent = (await settings.ResolveAsync(SettingKeys.AttendanceLiveMinPercent, classCourse.OrgUnitPath, ct)).Value;
        var lateAfter = (await settings.ResolveAsync(SettingKeys.AttendanceLateAfterMinutes, classCourse.OrgUnitPath, ct)).Value;
        var roster = await LiveRoster.StudentIdsAsync(db, live, date, ct);
        var participants = await db.Set<LiveSessionParticipant>().Where(p => p.SessionId == liveSessionId).ToListAsync(ct);

        var session = AttendanceSession.ForLive(classCourse, date, liveSessionId);
        db.Add(session);
        foreach (var student in roster)
        {
            var stays = participants.Where(p => p.MatchState == LiveSessionRules.Matched && p.StudentId == student)
                .Select(p => new PresenceSegment(p.JoinedAtUtc, p.LeftAtUtc));
            db.Add(new AttendanceRecord(session, student, PresenceCalculator.Evaluate(live.StartsAtUtc, live.EndsAtUtc, stays, minPercent, lateAfter).Status, null));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AttendanceSessionGenerator.IsDuplicate(ex))
        {
            return false; // another run created the proposal first
        }

        return true;
    }
}

/// <summary>The roster of a live session: the students of the class course enrolled on its local date, narrowed to the group when the lesson targets one (L11 item 7).</summary>
internal static class LiveRoster
{
    public static async Task<List<Guid>> StudentIdsAsync(LmsDbContext db, LiveSession live, DateOnly date, CancellationToken ct)
    {
        var students = await new AttendanceRoster(db).StudentIdsAsync(live.ClassCourseId, date, ct);
        var module = live.ModuleId is { } moduleId ? await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == moduleId, ct) : null;
        if (module is not { Target: "Group", GroupId: { } groupId })
        {
            return students;
        }

        var inGroup = await db.Set<GroupMember>().Where(g => g.GroupId == groupId).Select(g => g.StudentId).ToListAsync(ct);
        return students.Where(inGroup.Contains).ToList();
    }
}
