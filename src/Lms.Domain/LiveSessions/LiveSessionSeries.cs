using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.LiveSessions;

/// <summary>
/// The authoring unit of a recurring live session (FR-VC-01): the weekly rule its occurrences were generated from. Unit fields
/// are the class course's. A single session has no series.
/// </summary>
[Audited]
public sealed class LiveSessionSeries : UnitOwnedEntity
{
    private LiveSessionSeries()
    {
        DaysOfWeek = TimeZoneId = string.Empty;
    }

    public LiveSessionSeries(ClassCourse classCourse, Guid lessonKey, RecurrenceRule rule)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        LessonKey = lessonKey;
        DaysOfWeek = string.Empty;
        TimeZoneId = string.Empty;
        Apply(rule);
    }

    public Guid ClassCourseId { get; private set; }
    public Guid LessonKey { get; private set; }
    public int IntervalWeeks { get; private set; }

    /// <summary>Weekday names, comma separated, in week order.</summary>
    public string DaysOfWeek { get; private set; }

    public DateTime? UntilUtc { get; private set; }
    public int? Count { get; private set; }
    public string TimeZoneId { get; private set; }

    public IReadOnlyList<string> Days => DaysOfWeek.Length == 0 ? [] : DaysOfWeek.Split(',');

    public RecurrenceRule Rule => new(IntervalWeeks, Days, UntilUtc, Count, TimeZoneId);

    public void Apply(RecurrenceRule rule)
    {
        IntervalWeeks = rule.IntervalWeeks;
        DaysOfWeek = string.Join(',', rule.DaysOfWeek.Distinct().OrderBy(d => Enum.Parse<DayOfWeek>(d)));
        UntilUtc = rule.UntilUtc;
        Count = rule.Count;
        TimeZoneId = rule.TimeZoneId;
    }
}
