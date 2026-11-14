using Lms.Domain.Abstractions;
using Lms.Infrastructure.Access;
using Lms.Domain.Classes;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>
/// Builds the timetable views of a date range (FR-TT-04, FR-TT-05, M5 Task 15): each published slot is a card on every date of the range that falls on its
/// weekday inside its term, with the dated exception applied (a cancelled lesson is kept and marked, a moved one sits in its new period, a substitution names the
/// substitute). Only published slots are ever passed in, and a card carries names only: no user id, no note, no slot state.
/// </summary>
public sealed class TimetableViewService(LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings, IClock clock)
{
    public const int MaxDays = 31;

    /// <param name="onlyDates">For a slot the caller does not own but substitutes on: the only dates it appears on (the exception dates); other slots show on every date.</param>
    public async Task<TimetableViewDto> BuildAsync(
        IReadOnlyList<TimetableSlot> slots, DateOnly from, DateOnly to, Guid organizationId, IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>>? onlyDates, CancellationToken ct)
    {
        var slotIds = slots.Select(s => s.Id).ToList();
        // ISOLATION-BYPASS: the exceptions of a lesson one substitutes on sit in another school of the client; limited to the slots passed in (already the caller's own),
        // the client, live rows and the date range.
        var exceptions = (await db.Set<TimetableException>().IgnoreQueryFilters()
                .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && slotIds.Contains(e.SlotId) && e.Date >= from && e.Date <= to).ToListAsync(ct))
            .ToDictionary(e => (e.SlotId, e.Date));
        var dtos = (await timetable.ToDtosAsync(organizationId, slots, null, ct)).ToDictionary(d => d.Id);
        var classIds = slots.Select(s => s.ClassId).Distinct().ToList();
        // ISOLATION-BYPASS: a lesson one substitutes on sits in a class of another school of the client; the slots passed in are already limited to the caller (own,
        // own class, own child or own substitution), so only the class names and ids of those slots are read, limited to the client and live rows.
        var classes = await db.Set<Class>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
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
        var days = slots.Count == 0
            ? WorkingDayNames.Default
            : (await settings.ResolveAsync(SettingKeys.TimetableWorkingDays, slots[0].OrgUnitPath, ct)).Value;
        return new TimetableViewDto(from, to, days, periodRows, cards.OrderBy(c => c.Date).ThenBy(c => c.PeriodId).ToList(), clock.UtcNow);
    }

    /// <summary>The published slots with these ids across the client: the lessons of the caller's own substitutions (see <see cref="Access.SubstituteRelationship"/>).</summary>
    public async Task<List<TimetableSlot>> SubstitutedSlotsAsync(Guid organizationId, IReadOnlyCollection<Guid> slotIds, CancellationToken ct) =>
        // ISOLATION-BYPASS: the slot a teacher covers is in another class or school of the client; limited to the ids of the caller's own substitutions, published and live.
        await db.Set<TimetableSlot>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.OrganizationId == organizationId && s.State == TimetableSlot.PublishedState && slotIds.Contains(s.Id)).ToListAsync(ct);

    private async Task<IReadOnlyList<PeriodDto>> PeriodsAsync(IReadOnlyList<TimetableSlot> slots, List<TimetableCardDto> cards, CancellationToken ct)
    {
        var result = new Dictionary<Guid, PeriodDto>();
        foreach (var unitId in slots.Select(s => s.OrgUnitId).Distinct())
        {
            // ISOLATION-BYPASS: the unit of a substituted lesson can be outside the caller's scope; only its periods are read, limited to the unit of a slot already
            // passed in (the caller's own), live rows.
            var unit = await db.OrgUnits.IgnoreQueryFilters().SingleAsync(u => !u.IsDeleted && u.Id == unitId, ct);
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
