using Lms.Domain.AcademicYears;
using Lms.Domain.Calendar;
using Lms.Domain.Classes;
using Lms.Domain.Identity;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>What the caller asked for: kind and its details. The date is part of the key and never changes after the exception is created.</summary>
public sealed record ExceptionInput(DateOnly Date, string Kind, Guid? SubstituteUserId, Guid? NewPeriodId, string? Note);

/// <summary>The outcome of checking an exception: a problem code for the API, or nothing.</summary>
public sealed record ExceptionCheck(string? Problem, int Status)
{
    public bool IsValid => Problem is null;

    public static ExceptionCheck Ok { get; } = new(null, 0);

    public static ExceptionCheck Invalid(string problem, int status = 400) => new(problem, status);
}

/// <summary>
/// The rules of a dated exception (FR-TT-05, L15): the slot is published; the date is a school day (working weekday, not a holiday) on the slot's weekday
/// inside its term and has no other exception; a substitute is another active teacher of the school who is free in that period that day; a moved lesson
/// goes to another period of the unit. The base slot is never touched. The calendar answers the day questions in the client time zone.
/// </summary>
public sealed class ExceptionService(LmsDbContext db, IClientCalendar calendar, PeriodService periods)
{
    public async Task<ExceptionCheck> CheckAsync(TimetableSlot slot, Term term, ExceptionInput input, Guid? existingId, CancellationToken ct)
    {
        if (!slot.IsPublished)
        {
            return ExceptionCheck.Invalid(ProblemCodes.timetable_not_published, 409);
        }

        if (!ExceptionKinds.All.Contains(input.Kind) || input.Note?.Length > TimetableLimits.MaxNoteLength)
        {
            return ExceptionCheck.Invalid(ProblemCodes.validation_failed);
        }

        var today = await calendar.LocalTodayAsync(slot.OrganizationId, ct);
        if (input.Date < today) // a lesson that already took place cannot be cancelled, moved or covered afterwards
        {
            return ExceptionCheck.Invalid(ProblemCodes.exception_date_invalid);
        }

        if (input.Date.DayOfWeek != slot.Day || input.Date < term.StartDate || input.Date > term.EndDate
            || !await calendar.IsWorkingDayAsync(slot.OrgUnitId, input.Date, ct) || await calendar.IsHolidayAsync(slot.OrgUnitId, input.Date, ct)
            || await db.Set<TimetableException>().AnyAsync(e => e.SlotId == slot.Id && e.Date == input.Date && e.Id != existingId, ct))
        {
            return ExceptionCheck.Invalid(ProblemCodes.exception_date_invalid);
        }

        return input.Kind switch
        {
            ExceptionKinds.Substitute => await CheckSubstituteAsync(slot, input, existingId, ct),
            ExceptionKinds.Moved => await CheckMovedAsync(slot, input, ct),
            _ => ExceptionCheck.Ok,
        };
    }

    private async Task<ExceptionCheck> CheckSubstituteAsync(TimetableSlot slot, ExceptionInput input, Guid? existingId, CancellationToken ct)
    {
        if (input.SubstituteUserId is not { } substitute || substitute == slot.TeacherUserId)
        {
            return ExceptionCheck.Invalid(ProblemCodes.exception_substitute_invalid);
        }

        var active = await db.Users.AnyAsync(u => u.Id == substitute && u.Status == UserStatus.Active, ct);
        var teachesHere = await db.Set<ClassCourseTeacher>().AnyAsync(t => t.UserId == substitute, ct); // a teacher of the school (visible in the caller's scope)
        if (!active || !teachesHere)
        {
            return ExceptionCheck.Invalid(ProblemCodes.exception_substitute_invalid);
        }

        // Free in that period that day, anywhere in the client (a teacher can also teach in a sibling school): no slot of theirs (not itself cancelled or moved away that
        // date) and no other substitution in the same period.
        // ISOLATION-BYPASS: the busy check must see slots in classes of sibling schools outside the caller scope; limited to the client, live published slots of one
        // teacher in one term and weekday; only a yes or no (busy or not) leaves.
        var ownSlots = await db.Set<TimetableSlot>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.OrganizationId == slot.OrganizationId && s.TeacherUserId == substitute && s.TermId == slot.TermId && s.Day == slot.Day
                        && s.State == TimetableSlot.PublishedState)
            .Select(s => new { s.Id, s.PeriodId }).ToListAsync(ct);
        foreach (var own in ownSlots)
        {
            // ISOLATION-BYPASS: the exception of one of those slots, same limits (client, live, one slot and date).
            var moved = await db.Set<TimetableException>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => !e.IsDeleted && e.OrganizationId == slot.OrganizationId && e.SlotId == own.Id && e.Date == input.Date, ct);
            var period = moved?.Kind == TimetableException.MovedKind && moved.NewPeriodId is { } p ? p : own.PeriodId;
            if (moved?.Kind != TimetableException.CancelledKind && period == slot.PeriodId)
            {
                return ExceptionCheck.Invalid(ProblemCodes.exception_substitute_invalid);
            }
        }

        // ISOLATION-BYPASS: another substitution of the same teacher in a sibling school, same limits; only a yes or no leaves.
        var busyAsSubstitute = await (
            from e in db.Set<TimetableException>().IgnoreQueryFilters()
            join s in db.Set<TimetableSlot>().IgnoreQueryFilters() on e.SlotId equals s.Id
            where !e.IsDeleted && !s.IsDeleted && e.OrganizationId == slot.OrganizationId && e.SubstituteUserId == substitute && e.Date == input.Date && s.PeriodId == slot.PeriodId && e.Id != existingId && e.SlotId != slot.Id
            select e.Id).AnyAsync(ct);
        return busyAsSubstitute ? ExceptionCheck.Invalid(ProblemCodes.exception_substitute_invalid) : ExceptionCheck.Ok;
    }

    private async Task<ExceptionCheck> CheckMovedAsync(TimetableSlot slot, ExceptionInput input, CancellationToken ct)
    {
        if (input.NewPeriodId is not { } period || period == slot.PeriodId)
        {
            return ExceptionCheck.Invalid(ProblemCodes.validation_failed);
        }

        var unit = await db.OrgUnits.SingleAsync(u => u.Id == slot.OrgUnitId, ct);
        var (effective, _) = await periods.EffectiveAsync(unit, ct);
        return effective.Any(p => p.Id == period) ? ExceptionCheck.Ok : ExceptionCheck.Invalid(ProblemCodes.validation_failed);
    }
}
