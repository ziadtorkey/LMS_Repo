using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.LiveSessions;

/// <summary>
/// Who attended a live session, as Zoom reported it (not audited: volume). Matching to a student is by verified email only and
/// only for an authenticated participant (Decision 7); everything else stays <c>Guest</c> and is never attached to a student.
/// </summary>
public sealed class LiveSessionParticipant : UnitOwnedEntity
{
    private LiveSessionParticipant()
    {
        DisplayName = MatchState = string.Empty;
    }

    public LiveSessionParticipant(
        LiveSession session, ClassCourse classCourse, string displayName, string matchState, Guid? studentId, DateTime joinedAtUtc,
        DateTime? leftAtUtc, int durationSeconds)
    {
        PlaceLike(classCourse);
        SessionId = session.Id;
        DisplayName = displayName;
        MatchState = matchState;
        StudentId = studentId;
        JoinedAtUtc = joinedAtUtc;
        LeftAtUtc = leftAtUtc;
        DurationSeconds = durationSeconds;
    }

    public Guid SessionId { get; private set; }
    public Guid? StudentId { get; private set; }
    public string DisplayName { get; private set; }
    public string MatchState { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }
    public DateTime? LeftAtUtc { get; private set; }
    public int DurationSeconds { get; private set; }
}
