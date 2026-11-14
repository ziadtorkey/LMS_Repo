using Lms.Domain.Timetable;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Timetable;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Dated exceptions of a published slot (M5 Task 15): read with Timetable.View, written with Timetable.Manage, checked at the slot's unit. A Teacher reads
/// but cannot write (403); School 2 and the other client reach neither the slot nor its exceptions (404).
/// </summary>
public sealed class TimetableExceptionProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("timetable exceptions list", HttpMethod.Get, Routes.TimetableSlotExceptionsPattern,
            async (w, _) => new ProbeRequest(Routes.TimetableSlotExceptions((await NewSlotAsync(w)).Slot.Id)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("timetable exception create", HttpMethod.Post, Routes.TimetableSlotExceptionsPattern,
            async (w, _) =>
            {
                var (slot, date, _) = await NewSlotAsync(w);
                return new ProbeRequest(Routes.TimetableSlotExceptions(slot.Id), new SaveTimetableExceptionRequest(date, ExceptionKinds.Cancelled, null, null, null));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("timetable exception update", HttpMethod.Put, Routes.TimetableSlotExceptionPattern,
            async (w, _) =>
            {
                var (slot, date, exception) = await NewSlotAsync(w, withException: true);
                return new ProbeRequest(Routes.TimetableSlotException(slot.Id, exception!.Id), new SaveTimetableExceptionRequest(date, ExceptionKinds.Cancelled, null, null, "note"));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("timetable exception delete", HttpMethod.Delete, Routes.TimetableSlotExceptionPattern,
            async (w, _) =>
            {
                var (slot, _, exception) = await NewSlotAsync(w, withException: true);
                return new ProbeRequest(Routes.TimetableSlotException(slot.Id, exception!.Id));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    /// <summary>A published Sunday slot at School1 and the first Sunday after the term starts; optionally with a cancelled exception on that date.</summary>
    private static async Task<(TimetableSlot Slot, DateOnly Date, TimetableException? Exception)> NewSlotAsync(M1ProbeWorld w, bool withException = false)
    {
        var factory = w.Callers.Factory;
        var world = await TimetableKit.NewWorldAsync(factory, w.Callers.A.School1);
        var date = world.Term.StartDate.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = new TimetableSlot(world.Class, world.Math.Id, world.Term.Id, world.MathTeacher, DayOfWeek.Sunday, world.First.Id, null);
        slot.Publish();
        db.Add(slot);
        TimetableException? exception = null;
        if (withException)
        {
            exception = new TimetableException(slot, date, TimetableException.CancelledKind, null, null, null);
            db.Add(exception);
        }

        await db.SaveChangesAsync();
        return (slot, date, exception);
    }
}
