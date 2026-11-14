using Lms.Domain.Attendance;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Attendance;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Attendance.Live;

/// <summary>What the page needs of a live proposal: the minutes and first join per student, the guests to map, and whether the participants were imported.</summary>
public sealed record LiveProposalView(
    IReadOnlyDictionary<Guid, PresenceResult> Presence, IReadOnlyList<UnmatchedParticipantDto> Unmatched, bool ParticipantsAvailable);

/// <summary>The outcome of saving manual mappings: ok, or the problem code (400 <c>validation_failed</c>).</summary>
public sealed record LiveMappingOutcome(bool Ok, string? ProblemCode);

/// <summary>
/// The request side of the live proposal (M5 Task 18, L11): reads the participant rows (never calls the provider again, item 2), lists only the guests for mapping
/// (item 1, 11), keeps manual mappings in <see cref="LiveAttendanceMapping"/> (item 8) and re-proposes only the students a mapping touched, so the teacher edits of
/// everyone else stay. The caller is already checked against the attendance session by the endpoint.
/// </summary>
public sealed class LiveProposalService(LmsDbContext db, ISettingsResolver settings, AttendanceRoster roster)
{
    private sealed record Loaded(
        LiveSession Live, List<LiveSessionParticipant> Participants, List<LiveAttendanceMapping> Mappings, int MinPercent, int LateAfter);

    private async Task<Loaded?> LoadAsync(AttendanceSession session, CancellationToken ct)
    {
        if (session.LiveSessionId is not { } liveId)
        {
            return null;
        }

        var live = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == liveId, ct);
        if (live is null)
        {
            return null; // the live session was removed: a taken proposal stays valid, it just has no participants to show (item 9)
        }

        var participants = await db.Set<LiveSessionParticipant>().Where(p => p.SessionId == liveId).ToListAsync(ct);
        var mappings = await db.Set<LiveAttendanceMapping>().Where(m => m.AttendanceSessionId == session.Id).ToListAsync(ct);
        var min = (await settings.ResolveAsync(SettingKeys.AttendanceLiveMinPercent, session.OrgUnitPath, ct)).Value;
        var late = (await settings.ResolveAsync(SettingKeys.AttendanceLateAfterMinutes, session.OrgUnitPath, ct)).Value;
        return new Loaded(live, participants, mappings, min, late);
    }

    private static PresenceResult PresenceOf(Loaded l, Guid student)
    {
        var mapped = l.Mappings.Where(m => m.StudentProfileId == student).Select(m => m.ParticipantId).ToHashSet();
        var stays = l.Participants
            .Where(p => p.MatchState == LiveSessionRules.Matched && p.StudentId == student || mapped.Contains(p.Id))
            .Select(p => new PresenceSegment(p.JoinedAtUtc, p.LeftAtUtc));
        return PresenceCalculator.Evaluate(l.Live.StartsAtUtc, l.Live.EndsAtUtc, stays, l.MinPercent, l.LateAfter);
    }

    public async Task<LiveProposalView> ViewAsync(AttendanceSession session, IReadOnlyCollection<Guid> students, CancellationToken ct)
    {
        var l = await LoadAsync(session, ct);
        if (l is null)
        {
            return new LiveProposalView(new Dictionary<Guid, PresenceResult>(), [], false);
        }

        var presence = students.ToDictionary(s => s, s => PresenceOf(l, s));
        var unmatched = l.Participants.Where(p => p.MatchState == LiveSessionRules.Guest)
            .OrderBy(p => p.JoinedAtUtc).ThenBy(p => p.Id)
            .Select(p => new UnmatchedParticipantDto(
                p.Id, p.DisplayName, (int)Math.Round(p.DurationSeconds / 60.0), p.JoinedAtUtc, l.Mappings.FirstOrDefault(m => m.ParticipantId == p.Id)?.StudentProfileId))
            .ToList();
        return new LiveProposalView(presence, unmatched, l.Live.ParticipantsImportedAtUtc is not null);
    }

    /// <summary>Saves the mappings of guests to roster students and re-proposes the students whose mapping changed (a proposal only; a taken session keeps its marks).</summary>
    public async Task<LiveMappingOutcome> MapAsync(AttendanceSession session, IReadOnlyList<LiveMappingEntry> entries, CancellationToken ct)
    {
        var l = await LoadAsync(session, ct);
        var students = (await roster.StudentIdsAsync(session.ClassCourseId, session.Date, ct)).ToHashSet();
        var guests = l?.Participants.Where(p => p.MatchState == LiveSessionRules.Guest).Select(p => p.Id).ToHashSet() ?? [];
        if (l is null || entries.Any(e => !guests.Contains(e.ParticipantId) || e.StudentId is { } s && !students.Contains(s))
            || entries.Select(e => e.ParticipantId).Distinct().Count() != entries.Count)
        {
            return new LiveMappingOutcome(false, "validation_failed"); // only listed guests, only roster students, once each
        }

        var touched = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            var existing = l.Mappings.FirstOrDefault(m => m.ParticipantId == entry.ParticipantId);
            if (existing is null)
            {
                var created = new LiveAttendanceMapping(session, entry.ParticipantId, entry.StudentId);
                db.Add(created);
                l.Mappings.Add(created);
            }
            else
            {
                if (existing.StudentProfileId is { } previous)
                {
                    touched.Add(previous);
                }

                existing.MapTo(entry.StudentId);
            }

            if (entry.StudentId is { } student)
            {
                touched.Add(student);
            }
        }

        if (session.IsProposal)
        {
            var records = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == session.Id && touched.Contains(r.StudentProfileId)).ToListAsync(ct);
            foreach (var record in records)
            {
                record.Set(PresenceOf(l, record.StudentProfileId).Status, record.Note);
            }
        }

        session.RecordProposalEdit();
        return new LiveMappingOutcome(true, null);
    }
}
