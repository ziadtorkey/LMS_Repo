using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.LiveSessions;

/// <summary>
/// One live session occurrence of a class course (FR-VC-01). Its lesson module (Kind <c>LiveSession</c>, <c>RefId</c> = this id)
/// owns title, visibility, target and completion rule; this row owns the time, the provider meeting and the recording. The join
/// and start URLs are held only protected (Data Protection) and never returned in a list or by a read.
/// </summary>
[Audited]
public sealed class LiveSession : UnitOwnedEntity
{
    private LiveSession()
    {
        Provider = Status = string.Empty;
    }

    public LiveSession(
        ClassCourse classCourse, Guid lessonKey, Guid? seriesId, int? occurrenceIndex, string provider, DateTime startsAtUtc,
        int durationMinutes)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        LessonKey = lessonKey;
        SeriesId = seriesId;
        OccurrenceIndex = occurrenceIndex;
        Provider = provider;
        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
        Status = LiveSessionRules.SyncFailed; // until the provider has confirmed the meeting
    }

    public Guid ClassCourseId { get; private set; }
    public Guid LessonKey { get; private set; }
    public Guid? SeriesId { get; private set; }
    public int? OccurrenceIndex { get; private set; }
    public Guid? ModuleId { get; private set; }
    public string Provider { get; private set; }
    public string? ExternalMeetingId { get; private set; }

    /// <summary>The pool host the meeting was created under; never exposed.</summary>
    public string? HostUserKey { get; private set; }

    public DateTime StartsAtUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public string Status { get; private set; }

    /// <summary>Data Protection ciphertext of the join URL.</summary>
    public string? JoinUrlProtected { get; private set; }

    /// <summary>Data Protection ciphertext of the host start URL.</summary>
    public string? StartUrlProtected { get; private set; }

    public string? RecordingUrl { get; private set; }
    public string? RecordingSource { get; private set; }
    public DateTime? ReminderSentAtUtc { get; private set; }

    public DateTime EndsAtUtc => StartsAtUtc.AddMinutes(DurationMinutes);

    public void AttachModule(Guid moduleId) => ModuleId = moduleId;

    public void Reschedule(DateTime startsAtUtc, int durationMinutes)
    {
        StartsAtUtc = startsAtUtc;
        DurationMinutes = durationMinutes;
        ReminderSentAtUtc = null;
    }

    public void SetOccurrence(Guid? seriesId, int? occurrenceIndex)
    {
        SeriesId = seriesId;
        OccurrenceIndex = occurrenceIndex;
    }

    /// <summary>The provider confirmed the meeting: it is scheduled and its links are kept protected.</summary>
    public void Synced(string externalMeetingId, string? hostUserKey, string joinUrlProtected, string startUrlProtected)
    {
        ExternalMeetingId = externalMeetingId;
        HostUserKey = hostUserKey;
        JoinUrlProtected = joinUrlProtected;
        StartUrlProtected = startUrlProtected;
        Status = LiveSessionRules.Scheduled;
    }

    /// <summary>The provider could not be reached or refused: the row is kept, the sweep retries.</summary>
    public void SyncFailed() => Status = LiveSessionRules.SyncFailed;

    public void Cancel() => Status = LiveSessionRules.Cancelled;
}
