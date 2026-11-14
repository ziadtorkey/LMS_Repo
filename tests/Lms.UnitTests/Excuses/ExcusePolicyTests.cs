using Lms.Domain.Excuses;

namespace Lms.UnitTests.Excuses;

public sealed class ExcusePolicyTests
{
    // Sunday to Thursday are school days (the default Saudi week); Friday and Saturday are not.
    private sealed class WeekCalendar(params DateOnly[] holidays) : ISchoolDayCalendar
    {
        public int SchoolDaysSince(DateOnly absenceDate, DateOnly today)
        {
            var count = 0;
            for (var d = absenceDate.AddDays(1); d <= today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday) && !holidays.Contains(d))
                {
                    count++;
                }
            }

            return count;
        }
    }

    private static readonly DateOnly Thursday = new(2027, 2, 4); // 2027-02-04 is a Thursday
    private static readonly ExcuseRules Rules = new(
        new HashSet<string> { "Illness", "Family", "Official", "Other" }, new HashSet<string> { "Illness" }, 5);

    private static ExcusePolicyResult Check(
        string reason, bool attachment, DateOnly from, DateOnly to, DateOnly today, DateOnly[] absences, ISchoolDayCalendar? calendar = null) =>
        ExcusePolicy.Evaluate(Rules, reason, attachment, from, to, today, absences, calendar ?? new WeekCalendar());

    [Fact]
    public void An_absence_inside_the_deadline_is_coverable()
    {
        var result = Check("Family", false, Thursday, Thursday, Thursday.AddDays(3), [Thursday]); // Fri, Sat, Sun = 1 school day

        Assert.True(result.IsAllowed);
        Assert.Equal([Thursday], result.CoverableDates);
    }

    [Fact]
    public void The_deadline_counts_school_days_not_calendar_days()
    {
        // Six calendar days later, but the weekend and a holiday do not count.
        var holiday = new DateOnly(2027, 2, 8);
        var today = Thursday.AddDays(6); // Wednesday 2027-02-10
        Assert.Equal(3, new WeekCalendar(holiday).SchoolDaysSince(Thursday, today));
        Assert.True(Check("Family", false, Thursday, Thursday, today, [Thursday], new WeekCalendar(holiday)).IsAllowed);
    }

    [Fact]
    public void Past_the_deadline_is_too_late()
    {
        var result = Check("Family", false, Thursday, Thursday, Thursday.AddDays(14), [Thursday]);

        Assert.Equal(ExcuseViolation.TooLate, result.Violation);
        Assert.Empty(result.CoverableDates);
    }

    [Fact]
    public void A_range_keeps_only_the_sessions_still_in_time()
    {
        var old = Thursday.AddDays(-14);
        var today = Thursday.AddDays(2);

        var result = Check("Family", false, old, Thursday, today, [old, Thursday]);

        Assert.True(result.IsAllowed);
        Assert.Equal([Thursday], result.CoverableDates);
    }

    [Fact]
    public void A_reason_outside_the_allowed_set_is_refused()
    {
        var restricted = Rules with { AllowedReasons = new HashSet<string> { "Illness" } };

        var result = ExcusePolicy.Evaluate(restricted, "Family", false, Thursday, Thursday, Thursday, [Thursday], new WeekCalendar());

        Assert.Equal(ExcuseViolation.ReasonNotAllowed, result.Violation);
    }

    [Fact]
    public void An_attachment_is_required_only_for_the_configured_reasons()
    {
        Assert.Equal(ExcuseViolation.AttachmentRequired, Check("Illness", false, Thursday, Thursday, Thursday, [Thursday]).Violation);
        Assert.True(Check("Illness", true, Thursday, Thursday, Thursday, [Thursday]).IsAllowed);
        Assert.True(Check("Family", false, Thursday, Thursday, Thursday, [Thursday]).IsAllowed);
    }

    [Fact]
    public void Dates_in_the_future_or_reversed_are_invalid()
    {
        Assert.Equal(ExcuseViolation.DatesInvalid, Check("Family", false, Thursday, Thursday, Thursday.AddDays(-1), [Thursday]).Violation); // after today
        Assert.Equal(ExcuseViolation.DatesInvalid, Check("Family", false, Thursday, Thursday.AddDays(-1), Thursday, [Thursday]).Violation); // reversed
    }

    [Fact]
    public void No_absent_or_late_session_in_the_range_is_nothing_to_excuse()
    {
        Assert.Equal(ExcuseViolation.NothingToExcuse, Check("Family", false, Thursday, Thursday, Thursday, []).Violation);
        Assert.Equal(ExcuseViolation.NothingToExcuse, Check("Family", false, Thursday, Thursday, Thursday, [Thursday.AddDays(-3)]).Violation); // outside the range
    }
}
