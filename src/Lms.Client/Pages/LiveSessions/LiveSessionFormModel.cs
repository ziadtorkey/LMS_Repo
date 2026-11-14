using Lms.Client.Components.Content;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Pages.LiveSessions;

/// <summary>
/// Editable state of the schedule dialog. Date and time are the teacher's wall clock in <c>Zone</c> (the browser's time
/// zone); they are sent as UTC together with the zone's IANA id (Decision 6).
/// </summary>
public sealed class LiveSessionFormModel
{
    public ModuleFormModel Module { get; set; } = new() { CompletionRule = CompletionRules.None };
    public Guid? LessonKey { get; set; }
    public DateTime? Date { get; set; }
    public TimeSpan? Time { get; set; } = new(10, 0, 0);
    public int DurationMinutes { get; set; } = 60;
    public bool Weekly { get; set; }
    public int IntervalWeeks { get; set; } = 1;
    public HashSet<DayOfWeek> Days { get; } = [];
    public bool EndsOnDate { get; set; } = true;
    public DateTime? UntilDate { get; set; }
    public int? Count { get; set; }

    /// <summary>The first start, in UTC (null until a date and time are chosen).</summary>
    public DateTime? StartsUtc(TimeZoneInfo zone) => Date is null || Time is null ? null : ToUtc(Date.Value.Date + Time.Value, zone);

    /// <summary>
    /// The weekly rule for the request, or null for a single session. The end is the end of the chosen day in the teacher's
    /// zone, so a session on the last day is still part of the series.
    /// </summary>
    public LiveRecurrenceInput? Recurrence(TimeZoneInfo zone)
    {
        if (!Weekly) return null;
        var days = Days.OrderBy(d => (int)d).Select(d => d.ToString()).ToList();
        var until = EndsOnDate && UntilDate is { } d ? ToUtc(d.Date.AddDays(1).AddSeconds(-1), zone) : (DateTime?)null;
        return new LiveRecurrenceInput(IntervalWeeks, days, until, EndsOnDate ? null : Count, zone.Id);
    }

    public ModuleSettingsInput ToSettings() =>
        new(Module.TitleAr.Trim(), Module.TitleEn.Trim(), Module.Visibility,
            Module.Visibility == ModuleVisibilities.Scheduled ? Module.FromUtc : null,
            Module.Visibility == ModuleVisibilities.Scheduled ? Module.ToUtcValue : null,
            Module.Target, Module.GroupId, Module.CompletionRule, null);

    public static LiveSessionFormModel From(LiveSessionDto s, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(s.StartsAtUtc, DateTimeKind.Utc), zone);
        var m = new LiveSessionFormModel
        {
            LessonKey = s.LessonKey, Date = local.Date, Time = local.TimeOfDay, DurationMinutes = s.DurationMinutes,
            Module = new ModuleFormModel
            {
                TitleAr = s.Module.TitleAr, TitleEn = s.Module.TitleEn, Visibility = s.Module.Visibility, Target = s.Module.Target,
                GroupId = s.Module.GroupId, CompletionRule = s.Module.CompletionRule,
            },
        };
        if (s.Module.VisibleFromUtc is { } f) { var l = DateTime.SpecifyKind(f, DateTimeKind.Utc).ToLocalTime(); m.Module.FromDate = l.Date; m.Module.FromTime = l.TimeOfDay; }
        if (s.Module.VisibleToUtc is { } t) { var l = DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime(); m.Module.ToDate = l.Date; m.Module.ToTime = l.TimeOfDay; }
        return m;
    }

    public static DateTime ToUtc(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // skipped by the spring-forward gap
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), DateTimeKind.Utc);
    }
}
