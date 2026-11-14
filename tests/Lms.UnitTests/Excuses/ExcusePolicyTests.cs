using Lms.Domain.Calendar;
using Lms.Domain.Excuses;

namespace Lms.UnitTests.Excuses;

public sealed class ExcusePolicyTests
{
    private static readonly Guid Unit = Guid.Parse("0198a6f2-0000-7000-8000-000000000051");

    // Sunday to Thursday are school days (the default Saudi week); Friday and Saturday are not. Only the school-day count is used by the policy.
    private sealed class WeekCalendar(params DateOnly[] holidays) : IClientCalendar
    {
        public Task<int> SchoolDaysBetweenAsync(Guid orgUnitId, DateOnly after, DateOnly through, CancellationToken cancellationToken)
        {
            var count = 0;
            for (var d = after.AddDays(1); d <= through; d = d.AddDays(1))
            {
                if (IsSchoolDay(d))
                {
                    count++;
                }
            }

            return Task.FromResult(count);
        }

        public Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(TimeZoneInfo.Utc);

        public Task<DateOnly> LocalTodayAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateOnly> LocalDateAsync(Guid organizationId, DateTime instantUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsWorkingDayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(date.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday));

        public Task<bool> IsHolidayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(holidays.Contains(date));

        public Task<DateOnly> AddSchoolDaysAsync(Guid orgUnitId, DateOnly from, int schoolDays, CancellationToken cancellationToken) => throw new NotSupportedException();

        private bool IsSchoolDay(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday) && !holidays.Contains(d);
    }

    private static readonly DateOnly Thursday = new(2027, 2, 4); // 2027-02-04 is a Thursday
    private static readonly ExcuseRules Rules = new(
        new HashSet<string> { "Illness", "Family", "Official", "Other" }, new HashSet<string> { "Illness" }, 5);

    private static Task<ExcusePolicyResult> CheckAsync(
        string reason, bool attachment, DateOnly from, DateOnly to, DateOnly today, DateOnly[] absences, IClientCalendar? calendar = null) =>
        ExcusePolicy.EvaluateAsync(Rules, reason, attachment, from, to, today, absences, Unit, calendar ?? new WeekCalendar());

    private sealed class CountingCalendar : IClientCalendar
    {
        public int Calls { get; private set; }

        public Task<int> SchoolDaysBetweenAsync(Guid orgUnitId, DateOnly after, DateOnly through, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("No school day found within the look-ahead limit.");
        }

        public Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateOnly> LocalTodayAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateOnly> LocalDateAsync(Guid organizationId, DateTime instantUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsWorkingDayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsHolidayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DateOnly> AddSchoolDaysAsync(Guid orgUnitId, DateOnly from, int schoolDays, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task An_absence_older_than_the_look_back_is_too_late_without_asking_the_calendar()
    {
        var calendar = new CountingCalendar(); // it would throw (the real one cannot count beyond a year)
        var old = Thursday.AddDays(-(ExcusePolicy.MaxLookBackDays + 5));

        var result = await CheckAsync("Family", false, old, Thursday, Thursday, [old], calendar);

        Assert.Equal((ExcuseViolation.TooLate, 0), (result.Violation, calendar.Calls));
    }

    [Fact]
    public async Task An_absence_inside_the_deadline_is_coverable()
    {
        var result = await CheckAsync("Family", false, Thursday, Thursday, Thursday.AddDays(3), [Thursday]); // Fri, Sat, Sun = 1 school day

        Assert.True(result.IsAllowed);
        Assert.Equal([Thursday], result.CoverableDates);
    }

    [Fact]
    public async Task The_deadline_counts_school_days_not_calendar_days()
    {
        // Six calendar days later, but the weekend and a holiday do not count.
        var holiday = new DateOnly(2027, 2, 8);
        var today = Thursday.AddDays(6); // Wednesday 2027-02-10
        Assert.Equal(3, await new WeekCalendar(holiday).SchoolDaysBetweenAsync(Unit, Thursday, today, CancellationToken.None));
        Assert.True((await CheckAsync("Family", false, Thursday, Thursday, today, [Thursday], new WeekCalendar(holiday))).IsAllowed);
    }

    [Fact]
    public async Task Past_the_deadline_is_too_late()
    {
        var result = await CheckAsync("Family", false, Thursday, Thursday, Thursday.AddDays(14), [Thursday]);

        Assert.Equal(ExcuseViolation.TooLate, result.Violation);
        Assert.Empty(result.CoverableDates);
    }

    [Fact]
    public async Task A_range_keeps_only_the_sessions_still_in_time()
    {
        var old = Thursday.AddDays(-14);
        var today = Thursday.AddDays(2);

        var result = await CheckAsync("Family", false, old, Thursday, today, [old, Thursday]);

        Assert.True(result.IsAllowed);
        Assert.Equal([Thursday], result.CoverableDates);
    }

    [Fact]
    public async Task A_reason_outside_the_allowed_set_is_refused()
    {
        var restricted = Rules with { AllowedReasons = new HashSet<string> { "Illness" } };

        var result = await ExcusePolicy.EvaluateAsync(restricted, "Family", false, Thursday, Thursday, Thursday, [Thursday], Unit, new WeekCalendar());

        Assert.Equal(ExcuseViolation.ReasonNotAllowed, result.Violation);
    }

    [Fact]
    public async Task An_attachment_is_required_only_for_the_configured_reasons()
    {
        Assert.Equal(ExcuseViolation.AttachmentRequired, (await CheckAsync("Illness", false, Thursday, Thursday, Thursday, [Thursday])).Violation);
        Assert.True((await CheckAsync("Illness", true, Thursday, Thursday, Thursday, [Thursday])).IsAllowed);
        Assert.True((await CheckAsync("Family", false, Thursday, Thursday, Thursday, [Thursday])).IsAllowed);
    }

    [Fact]
    public async Task Dates_in_the_future_or_reversed_are_invalid()
    {
        Assert.Equal(ExcuseViolation.DatesInvalid, (await CheckAsync("Family", false, Thursday, Thursday, Thursday.AddDays(-1), [Thursday])).Violation); // after today
        Assert.Equal(ExcuseViolation.DatesInvalid, (await CheckAsync("Family", false, Thursday, Thursday.AddDays(-1), Thursday, [Thursday])).Violation); // reversed
    }

    [Fact]
    public async Task No_absent_or_late_session_in_the_range_is_nothing_to_excuse()
    {
        Assert.Equal(ExcuseViolation.NothingToExcuse, (await CheckAsync("Family", false, Thursday, Thursday, Thursday, [])).Violation);
        Assert.Equal(ExcuseViolation.NothingToExcuse, (await CheckAsync("Family", false, Thursday, Thursday, Thursday, [Thursday.AddDays(-3)])).Violation); // outside the range
    }
}
