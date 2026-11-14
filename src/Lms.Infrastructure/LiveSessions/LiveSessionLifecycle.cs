using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.LiveSessions;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>
/// What happens to a session after it was scheduled (M4b Task 6): it starts, it ends, its participants are imported and matched,
/// and the attendance seam is told once. Called by the provider events and the polling job (Task 7) and therefore with the
/// system context; the caller names the session by id only.
/// </summary>
public sealed class LiveSessionLifecycle(
    LmsDbContext db, IMeetingProvider provider, ILiveSessionAttendanceSink sink, ILogger<LiveSessionLifecycle> logger)
{
    /// <summary><c>Scheduled</c> → <c>Live</c>; false when the session is gone or already left <c>Scheduled</c>.</summary>
    public async Task<bool> StartedAsync(Guid sessionId, CancellationToken ct) =>
        await db.Set<LiveSession>().Where(s => s.Id == sessionId && s.Status == LiveSessionRules.Scheduled)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, LiveSessionRules.Live), ct) > 0;

    /// <summary>
    /// <c>Scheduled</c> or <c>Live</c> → <c>Ended</c>, then the participants are imported and the attendance sink is called, once:
    /// a second call (a retried event, the polling fallback) finds the session already ended and does nothing. False when nothing changed.
    /// </summary>
    public async Task<bool> EndedAsync(Guid sessionId, CancellationToken ct)
    {
        var claimed = await db.Set<LiveSession>()
            .Where(s => s.Id == sessionId && (s.Status == LiveSessionRules.Scheduled || s.Status == LiveSessionRules.Live))
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, LiveSessionRules.Ended), ct);
        if (claimed == 0)
        {
            return false;
        }

        var session = await db.Set<LiveSession>().FirstAsync(s => s.Id == sessionId, ct);
        var participants = await ImportParticipantsAsync(session, ct);
        await sink.SessionEndedAsync(
            session.Id, session.ClassCourseId, session.StartsAtUtc, session.EndsAtUtc,
            participants.Select(p => new AttendanceParticipant(p.StudentId, p.MatchState, p.JoinedAtUtc, p.LeftAtUtc, p.DurationSeconds)).ToList(), ct);
        return true;
    }

    /// <summary>
    /// Replaces the session's participant rows with what the provider reports. Only an authenticated participant whose verified
    /// email equals the account email of a student enrolled in the class course is <c>Matched</c> (Decision 7); everyone else,
    /// including a guest who typed a student's email, is a <c>Guest</c> and is never attached to a student.
    /// </summary>
    public async Task<IReadOnlyList<LiveSessionParticipant>> ImportParticipantsAsync(LiveSession session, CancellationToken ct)
    {
        IReadOnlyList<MeetingParticipant> reported;
        try
        {
            reported = session.ExternalMeetingId is null ? [] : await provider.GetParticipantsAsync(session.ExternalMeetingId, ct);
        }
        catch (MeetingProviderException ex)
        {
            logger.LogWarning(ex, "Participants of live session {SessionId} could not be read", session.Id);
            return await db.Set<LiveSessionParticipant>().Where(p => p.SessionId == session.Id).ToListAsync(ct);
        }

        var classCourse = await db.Set<ClassCourse>().FirstAsync(c => c.Id == session.ClassCourseId, ct);
        var byEmail = await StudentsByEmailAsync(session.ClassCourseId, ct);
        db.RemoveRange(await db.Set<LiveSessionParticipant>().Where(p => p.SessionId == session.Id).ToListAsync(ct));
        var rows = reported.Select(p =>
        {
            Guid? student = p.Email is { Length: > 0 } email && byEmail.TryGetValue(email.Trim().ToUpperInvariant(), out var id) ? id : null;
            return new LiveSessionParticipant(
                session, classCourse, p.DisplayName.Length > 200 ? p.DisplayName[..200] : p.DisplayName,
                student is null ? LiveSessionRules.Guest : LiveSessionRules.Matched, student, p.JoinedAtUtc, p.LeftAtUtc, p.DurationSeconds);
        }).ToList();
        db.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return rows;
    }

    /// <summary>Account email (normalised) → student, for the active enrollments of the class course; an email two students share matches nobody.</summary>
    private async Task<Dictionary<string, Guid>> StudentsByEmailAsync(Guid classCourseId, CancellationToken ct)
    {
        var pairs = await (from e in db.Set<CourseEnrollment>()
                           join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                           join u in db.Users on s.UserId equals u.Id
                           where e.ClassCourseId == classCourseId && e.Status == CourseEnrollment.ActiveStatus && u.NormalizedEmail != null
                           select new { s.Id, u.NormalizedEmail }).ToListAsync(ct);
        return pairs.GroupBy(p => p.NormalizedEmail!).Where(g => g.Select(x => x.Id).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id);
    }
}
