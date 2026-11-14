using Lms.Domain.Calendar;

namespace Lms.Domain.Excuses;

/// <summary>The per-client rules of the excuse policy (settings <c>Excuses.AllowedReasons</c>, <c>AttachmentRequiredReasons</c>, <c>MaxDaysAfterAbsence</c>).</summary>
public sealed record ExcuseRules(IReadOnlySet<string> AllowedReasons, IReadOnlySet<string> AttachmentRequiredReasons, int MaxDaysAfterAbsence);

/// <summary>Why an excuse is outside the policy; the values equal <c>Lms.Shared.Excuses.ExcusePolicyViolations</c> (pinned by a unit test).</summary>
public static class ExcuseViolation
{
    public const string TooLate = "TooLate";
    public const string ReasonNotAllowed = "ReasonNotAllowed";
    public const string AttachmentRequired = "AttachmentRequired";
    public const string NothingToExcuse = "NothingToExcuse";
    public const string DatesInvalid = "DatesInvalid";
}

/// <summary>The outcome of a policy check: a violation, or the absence dates the excuse can cover.</summary>
public sealed record ExcusePolicyResult(string? Violation, IReadOnlyList<DateOnly> CoverableDates)
{
    public bool IsAllowed => Violation is null;
}

/// <summary>
/// The pure policy check of a submission (FR-EXC-02, D61, L13). <paramref name="absenceDates"/> are the dates of the student's <c>Absent</c> or <c>Late</c> sessions
/// inside the range the submitter chose (the caller looks them up); only those within the deadline are coverable, so an excuse over a long range keeps
/// the sessions that are still in time.
/// </summary>
public static class ExcusePolicy
{
    public static async Task<ExcusePolicyResult> EvaluateAsync(
        ExcuseRules rules, string reason, bool hasAttachment, DateOnly from, DateOnly to, DateOnly today,
        IReadOnlyCollection<DateOnly> absenceDates, Guid orgUnitId, IClientCalendar calendar, CancellationToken ct = default)
    {
        if (from > to || to > today)
        {
            return Refuse(ExcuseViolation.DatesInvalid);
        }

        if (!rules.AllowedReasons.Contains(reason))
        {
            return Refuse(ExcuseViolation.ReasonNotAllowed);
        }

        if (rules.AttachmentRequiredReasons.Contains(reason) && !hasAttachment)
        {
            return Refuse(ExcuseViolation.AttachmentRequired);
        }

        var inRange = absenceDates.Where(d => d >= from && d <= to).Distinct().Order().ToList();
        if (inRange.Count == 0)
        {
            return Refuse(ExcuseViolation.NothingToExcuse);
        }

        var inTime = new List<DateOnly>();
        foreach (var date in inRange)
        {
            if (await calendar.SchoolDaysBetweenAsync(orgUnitId, date, today, ct) <= rules.MaxDaysAfterAbsence)
            {
                inTime.Add(date);
            }
        }

        return inTime.Count == 0 ? Refuse(ExcuseViolation.TooLate) : new ExcusePolicyResult(null, inTime);
    }

    private static ExcusePolicyResult Refuse(string violation) => new(violation, []);
}
