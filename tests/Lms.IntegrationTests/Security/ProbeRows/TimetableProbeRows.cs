using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Timetable;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>A class's weekly timetable (M5 Task 14): read with Timetable.View, built, validated and published with Timetable.Manage, checked at the class's unit.</summary>
public sealed class TimetableProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("class timetable read", HttpMethod.Get, Routes.ClassTimetablePattern,
            async (w, _) =>
            {
                var world = await NewWorldAsync(w);
                return new ProbeRequest(Routes.ClassTimetable(world.Class.Id, world.Term.Id));
            },
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("class timetable slot create", HttpMethod.Post, Routes.ClassTimetableSlotsPattern,
            async (w, _) =>
            {
                var world = await NewWorldAsync(w);
                return new ProbeRequest(Routes.ClassTimetableSlots(world.Class.Id), TimetableKit.Slot(world, world.Math, world.MathTeacher, DayOfWeek.Sunday, world.First));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("class timetable slot update", HttpMethod.Put, Routes.ClassTimetableSlotPattern,
            async (w, _) =>
            {
                var (world, slot) = await NewSlotAsync(w);
                return new ProbeRequest(Routes.ClassTimetableSlot(world.Class.Id, slot.Id),
                    TimetableKit.Slot(world, world.Math, world.MathTeacher, DayOfWeek.Sunday, world.Second, version: slot.Version));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class timetable slot delete", HttpMethod.Delete, Routes.ClassTimetableSlotPattern,
            async (w, _) =>
            {
                var (world, slot) = await NewSlotAsync(w);
                return new ProbeRequest(Routes.ClassTimetableSlot(world.Class.Id, slot.Id));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("class timetable validate", HttpMethod.Get, Routes.ClassTimetableValidatePattern,
            async (w, _) =>
            {
                var world = await NewWorldAsync(w);
                return new ProbeRequest(Routes.ClassTimetableValidate(world.Class.Id, world.Term.Id));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class timetable publish", HttpMethod.Post, Routes.ClassTimetablePublishPattern,
            async (w, _) =>
            {
                var world = await NewWorldAsync(w);
                return new ProbeRequest(Routes.ClassTimetablePublish(world.Class.Id), new PublishTimetableRequest(world.Term.Id));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static async Task<TimetableWorld> NewWorldAsync(M1ProbeWorld w) =>
        await TimetableKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1);

    private static async Task<(TimetableWorld World, SlotDto Slot)> NewSlotAsync(M1ProbeWorld w)
    {
        var world = await NewWorldAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassTimetableSlots(world.Class.Id),
            TimetableKit.Slot(world, world.Math, world.MathTeacher, DayOfWeek.Sunday, world.First));
        var slot = created.GetProperty("slot");
        return (world, new SlotDto(slot.GetProperty("id").GetGuid(), world.Class.Id, world.Math.Id, world.Term.Id, "", "", world.MathTeacher, "",
            DayOfWeek.Sunday, world.First.Id, null, SlotStates.Draft, slot.GetProperty("version").GetInt32(), []));
    }
}
