using Lms.Domain.Abstractions;
using Lms.Domain.Calendar;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.LiveSessions;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>
/// Builds the timetable views of a date range (FR-TT-04, FR-TT-05, M5 Task 15): each published slot is a card on every date of the range that falls on its
/// weekday inside its term, with the dated exception applied (a cancelled lesson is kept and marked, a moved one sits in its new period, a substitution names the
/// substitute). Only published slots are ever passed in, and a card carries names only: no user id, no note, no slot state.
/// </summary>
public sealed class TimetableViewService(
    LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings, IClock clock, IClientCalendar calendar)
{
    public const int MaxDays = 31;

    /// <param name="studentId">The student the view is for (own or a child): a live session shows on a card only when its module is visible to that student; null means staff, who see every live session of their lessons.</param>
    /// <param name="staffSlotIds">Slots of the view the caller teaches or substitutes on: staff visibility even when the caller is also a student.</param>
    /// <param name="onlyDates">For a slot the caller does not own but substitutes on: the only dates it appears on (the exception dates); other slots show on every date.</param>
    public async Task<TimetableViewDto> BuildAsync(
        IReadOnlyList<TimetableSlot> slots, DateOnly from, DateOnly to, Guid organizationId, IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>>? onlyDates, CancellationToken ct,
        Guid? studentId = null, IReadOnlySet<Guid>? staffSlotIds = null)
    {
        var slotIds = slots.Select(s => s.Id).ToList();
        var exceptions = (await db.Set<TimetableException>().Where(e => slotIds.Contains(e.SlotId) && e.Date >= from && e.Date <= to).ToListAsync(ct))
            .ToDictionary(e => (e.SlotId, e.Date));
        var dtos = (await timetable.ToDtosAsync(organizationId, slots, null, ct)).ToDictionary(d => d.Id);
        var classIds = slots.Select(s => s.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var substituteIds = exceptions.Values.Where(e => e.SubstituteUserId != null).Select(e => e.SubstituteUserId!.Value).Distinct().ToList();
        var substituteNames = substituteIds.Count == 0
            ? []
            : await db.Users.Where(u => substituteIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayNameEn.Length > 0 ? u.DisplayNameEn : u.DisplayNameAr, ct);

        var termDates = new Dictionary<(Guid ClassId, Guid TermId), (DateOnly Start, DateOnly End)?>();
        var cards = new List<TimetableCardDto>();
        foreach (var slot in slots)
        {
            if (!classes.TryGetValue(slot.ClassId, out var cls))
            {
                continue;
            }

            if (!termDates.TryGetValue((slot.ClassId, slot.TermId), out var window))
            {
                var term = await timetable.FindTermAsync(cls, slot.TermId, ct);
                window = term is null ? null : (term.StartDate, term.EndDate);
                termDates[(slot.ClassId, slot.TermId)] = window;
            }

            if (window is null)
            {
                continue;
            }

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (date.DayOfWeek != slot.Day || date < window.Value.Start || date > window.Value.End
                    || (onlyDates is not null && onlyDates.TryGetValue(slot.Id, out var allowed) && !allowed.Contains(date)))
                {
                    continue;
                }

                exceptions.TryGetValue((slot.Id, date), out var exception);
                var dto = dtos[slot.Id];
                var period = exception is { Kind: TimetableException.MovedKind, NewPeriodId: { } moved } ? moved : slot.PeriodId;
                var substitute = exception?.SubstituteUserId is { } u ? substituteNames.GetValueOrDefault(u) : null;
                cards.Add(new TimetableCardDto(slot.Id, date, period, dto.CourseNameAr, dto.CourseNameEn, dto.TeacherName, slot.RoomName,
                    exception?.Kind, substitute, cls.NameAr, cls.NameEn));
            }
        }

        var periodRows = await PeriodsAsync(slots, cards, ct);
        cards = await WithLiveSessionsAsync(cards, slots, periodRows, organizationId, from, to, studentId, staffSlotIds, ct);
        var days = slots.Count == 0
            ? WorkingDayNames.Default
            : (await settings.ResolveAsync(SettingKeys.TimetableWorkingDays, slots[0].OrgUnitPath, ct)).Value;
        return new TimetableViewDto(from, to, days, periodRows, cards.OrderBy(c => c.Date).ThenBy(c => c.PeriodId).ToList(), clock.UtcNow);
    }

    /// <summary>
    /// Fills <c>LiveSessionId</c> and <c>JoinOpensAtUtc</c> from the live sessions of the card class course on its client-local date (M5 Task 18). Several lessons of the
    /// same course on one day take the day live sessions in order of start, the n-th lesson (by period start) the n-th session. A student sees only a session whose module
    /// is visible to them (published or inside its window, for the whole class course or their group); a cancelled session never shows.
    /// </summary>
    private async Task<List<TimetableCardDto>> WithLiveSessionsAsync(
        List<TimetableCardDto> cards, IReadOnlyList<TimetableSlot> slots, IReadOnlyList<PeriodDto> periodRows, Guid organizationId, DateOnly from, DateOnly to,
        Guid? studentId, IReadOnlySet<Guid>? staffSlotIds, CancellationToken ct)
    {
        if (cards.Count == 0)
        {
            return cards;
        }

        var courseOf = slots.ToDictionary(s => s.Id, s => s.ClassCourseId);
        var courseIds = courseOf.Values.Distinct().ToList();
        var lower = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var upper = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(2);
        var sessions = await db.Set<LiveSession>()
            .Where(s => courseIds.Contains(s.ClassCourseId) && s.Status != LiveSessionRules.Cancelled && s.Status != LiveSessionRules.SyncFailed
                        && s.StartsAtUtc >= lower && s.StartsAtUtc <= upper)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.Id).ToListAsync(ct);
        if (sessions.Count == 0)
        {
            return cards;
        }

        var sessionIds = sessions.Select(s => (Guid?)s.Id).ToList();
        var modules = await db.Set<LessonModule>().Where(m => m.Kind == ModuleKinds.LiveSession && sessionIds.Contains(m.RefId)).ToListAsync(ct);
        var groups = studentId is { } sid
            ? await db.Set<GroupMember>().Where(g => g.StudentId == sid).Select(g => g.GroupId).ToListAsync(ct)
            : [];
        var now = clock.UtcNow;
        var local = new Dictionary<Guid, DateOnly>();
        foreach (var s in sessions)
        {
            local[s.Id] = await calendar.LocalDateAsync(organizationId, s.StartsAtUtc, ct);
        }

        var start = periodRows.ToDictionary(p => p.Id, p => p.StartTime);
        var result = new List<TimetableCardDto>(cards);
        foreach (var group in cards.Select((c, i) => (Card: c, Index: i)).GroupBy(x => (Course: courseOf[x.Card.SlotId], x.Card.Date)))
        {
            var day = sessions.Where(s => s.ClassCourseId == group.Key.Course && local[s.Id] == group.Key.Date).ToList();
            var ordered = group.OrderBy(x => start.GetValueOrDefault(x.Card.PeriodId)).ThenBy(x => x.Index).ToList();
            for (var n = 0; n < ordered.Count && n < day.Count; n++)
            {
                var session = day[n];
                var card = ordered[n].Card;
                var staff = studentId is null || staffSlotIds?.Contains(card.SlotId) == true;
                var module = modules.FirstOrDefault(m => m.RefId == session.Id);
                var visible = staff || module is not null && StudentSees(module, groups, now);
                if (!visible || card.ExceptionKind == TimetableException.CancelledKind)
                {
                    continue;
                }

                var window = (await settings.ResolveAsync(SettingKeys.LiveJoinWindowMinutes, session.OrgUnitPath, ct)).Value;
                result[ordered[n].Index] = card with { LiveSessionId = session.Id, JoinOpensAtUtc = session.StartsAtUtc.AddMinutes(-window) };
            }
        }

        return result;
    }

    private static bool StudentSees(LessonModule m, IReadOnlyList<Guid> groups, DateTime now) =>
        (m.Visibility == LessonModule.PublishedVisibility
         || m.Visibility == LessonModule.ScheduledVisibility && (m.VisibleFromUtc == null || m.VisibleFromUtc <= now) && (m.VisibleToUtc == null || now < m.VisibleToUtc))
        && (m.Target == LessonModule.ClassCourseTarget || m.GroupId is { } g && groups.Contains(g));

    private async Task<IReadOnlyList<PeriodDto>> PeriodsAsync(IReadOnlyList<TimetableSlot> slots, List<TimetableCardDto> cards, CancellationToken ct)
    {
        var result = new Dictionary<Guid, PeriodDto>();
        foreach (var unitId in slots.Select(s => s.OrgUnitId).Distinct())
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == unitId, ct);
            var (rows, inheritedFrom) = await periods.EffectiveAsync(unit, ct);
            foreach (var p in rows)
            {
                result[p.Id] = new PeriodDto(p.Id, p.OrgUnitId, p.NameAr, p.NameEn, p.StartTime, p.EndTime, p.Order, inheritedFrom);
            }
        }

        var used = cards.Select(c => c.PeriodId).ToHashSet();
        return result.Values.Where(p => used.Contains(p.Id)).OrderBy(p => p.Order).ToList();
    }
}
