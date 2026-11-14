using Lms.Client.Services.Api;
using Lms.Shared.Timetable;

namespace Lms.Client.Services.Timetable;

/// <summary>
/// In-memory timetable for <c>Lms:UseFakeApi=true</c> and the tests. It answers conflicts the way the server does (the client never predicts them):
/// the same class on the same day and period, or the same teacher in another class; Block refuses with 409 <c>timetable_conflict</c> and its clashes, Warn
/// saves and returns warnings with a Conflict badge. A stale version is 409 <c>concurrency_conflict</c>; a period in use cannot be deleted.
/// </summary>
public sealed class FakeTimetableApi : ITimetableApi
{
    public static readonly Guid ClassId = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    public static readonly Guid TermId = Guid.Parse("e1000000-0000-0000-0000-000000000001");
    public static readonly Guid UnitId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Maths = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid Arabic = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid Science = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid Saleh = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid Nasser = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid Omar = Guid.Parse("b1000000-0000-0000-0000-000000000003");

    private readonly List<PeriodDto> _periods = [];
    private readonly List<SlotDto> _slots = [];
    private readonly List<SlotDto> _otherClasses = []; // lessons of other classes: the source of teacher clashes

    public FakeTimetableApi()
    {
        for (var i = 1; i <= 7; i++)
        {
            var start = new TimeOnly(7, 30).AddMinutes(50 * (i - 1));
            _periods.Add(new PeriodDto(PeriodId(i), UnitId, $"الحصة {i}", $"Period {i}", start, start.AddMinutes(45), i, null));
        }

        _slots.Add(Slot(Maths, "الرياضيات", "Mathematics", Saleh, "A. Saleh", DayOfWeek.Sunday, 1, "101", SlotStates.Published));
        _slots.Add(Slot(Arabic, "اللغة العربية", "Arabic", Nasser, "M. Nasser", DayOfWeek.Sunday, 2, "101", SlotStates.Published));
        _slots.Add(Slot(Science, "العلوم", "Science", Omar, "L. Omar", DayOfWeek.Monday, 1, null, SlotStates.Draft));
        _otherClasses.Add(Slot(Guid.NewGuid(), "الرياضيات", "Mathematics (5B)", Saleh, "A. Saleh", DayOfWeek.Monday, 2, "102", SlotStates.Published) with { ClassId = Guid.NewGuid() });
    }

    public static Guid PeriodId(int order) => Guid.Parse($"d1000000-0000-0000-0000-{order:D12}");

    /// <summary>Block refuses a clashing save; Warn saves it with the warnings.</summary>
    public string ConflictMode { get; set; } = ConflictModes.Block;

    public IReadOnlyList<string> WorkingDays { get; set; } = WorkingDayNames.Default;

    public IReadOnlyList<SlotDto> Slots => _slots;

    public int PublishCalls { get; private set; }

    public (Guid Source, Guid Target)? LastCopy { get; private set; }

    public Task<IReadOnlyList<PeriodDto>> GetPeriodsAsync(Guid unitId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PeriodDto>>(_periods.OrderBy(p => p.Order).ToList());

    public Task<PeriodDto> CreatePeriodAsync(Guid unitId, SavePeriodRequest request, CancellationToken ct = default)
    {
        Validate(request, null);
        var period = new PeriodDto(Guid.NewGuid(), unitId, request.NameAr, request.NameEn, request.StartTime, request.EndTime, request.Order, null);
        _periods.Add(period);
        return Task.FromResult(period);
    }

    public Task<PeriodDto> UpdatePeriodAsync(Guid unitId, Guid periodId, SavePeriodRequest request, CancellationToken ct = default)
    {
        Validate(request, periodId);
        var index = _periods.FindIndex(p => p.Id == periodId);
        if (index < 0) throw Problem(404, "period_not_found");
        _periods[index] = _periods[index] with { NameAr = request.NameAr, NameEn = request.NameEn, StartTime = request.StartTime, EndTime = request.EndTime, Order = request.Order };
        return Task.FromResult(_periods[index]);
    }

    public Task DeletePeriodAsync(Guid unitId, Guid periodId, CancellationToken ct = default)
    {
        if (_slots.Any(s => s.PeriodId == periodId)) throw Problem(409, "period_in_use");
        _periods.RemoveAll(p => p.Id == periodId);
        return Task.CompletedTask;
    }

    public Task<ClassTimetableDto> GetClassTimetableAsync(Guid classId, Guid termId, CancellationToken ct = default) => Task.FromResult(Snapshot());

    public Task<SaveSlotResultDto> CreateSlotAsync(Guid classId, SaveSlotRequest request, CancellationToken ct = default)
    {
        var slot = new SlotDto(Guid.NewGuid(), classId, request.ClassCourseId, request.TermId, CourseName(request.ClassCourseId).Ar, CourseName(request.ClassCourseId).En,
            request.TeacherUserId, TeacherName(request.TeacherUserId), request.DayOfWeek, request.PeriodId, request.RoomName, SlotStates.Draft, 1, []);
        return Task.FromResult(Save(slot, null));
    }

    public Task<SaveSlotResultDto> UpdateSlotAsync(Guid classId, Guid slotId, SaveSlotRequest request, CancellationToken ct = default)
    {
        var existing = _slots.FirstOrDefault(s => s.Id == slotId) ?? throw Problem(404, "slot_not_found");
        if (request.Version is { } v && v != existing.Version) throw Problem(409, "concurrency_conflict");
        var changed = existing with { DayOfWeek = request.DayOfWeek, PeriodId = request.PeriodId, RoomName = request.RoomName, TeacherUserId = request.TeacherUserId,
            TeacherName = TeacherName(request.TeacherUserId), ClassCourseId = request.ClassCourseId, Version = existing.Version + 1, State = SlotStates.Draft };
        return Task.FromResult(Save(changed, existing.Id));
    }

    public Task DeleteSlotAsync(Guid classId, Guid slotId, CancellationToken ct = default)
    {
        _slots.RemoveAll(s => s.Id == slotId);
        return Task.CompletedTask;
    }

    public Task<TimetableValidationDto> ValidateAsync(Guid classId, Guid termId, CancellationToken ct = default) =>
        Task.FromResult(new TimetableValidationDto(_slots.SelectMany(s => ConflictsOf(s, s.Id)).ToList()));

    public Task<PublishTimetableResultDto> PublishAsync(Guid classId, PublishTimetableRequest request, CancellationToken ct = default)
    {
        PublishCalls++;
        var drafts = _slots.Count(s => s.State == SlotStates.Draft);
        for (var i = 0; i < _slots.Count; i++) _slots[i] = _slots[i] with { State = SlotStates.Published, Badges = _slots[i].Badges.Where(b => b != SlotBadges.DraftChange).ToList() };
        return Task.FromResult(new PublishTimetableResultDto(drafts, drafts > 0));
    }

    public Task<CopyTermResultDto> CopyTermAsync(Guid classId, CopyTermRequest request, CancellationToken ct = default)
    {
        LastCopy = (request.SourceTermId, request.TargetTermId);
        return Task.FromResult(new CopyTermResultDto(2, [new CopySkippedDto(Guid.NewGuid(), CopySkipReasons.TeacherMissing, "العلوم", "Science")]));
    }

    public Task<(Stream Content, string FileName)> ExportAsync(Guid classId, Guid termId, CancellationToken ct = default) =>
        Task.FromResult<(Stream, string)>((new MemoryStream([0x50, 0x4B, 0x03, 0x04]), "timetable.xlsx"));

    private SaveSlotResultDto Save(SlotDto slot, Guid? replacing)
    {
        var conflicts = ConflictsOf(slot, replacing);
        if (conflicts.Count > 0 && ConflictMode == ConflictModes.Block) throw new ApiProblemException(409, "timetable_conflict", null, null, conflicts: conflicts);
        if (conflicts.Count > 0) slot = slot with { Badges = [.. slot.Badges, SlotBadges.Conflict] };
        if (replacing is { } id) _slots[_slots.FindIndex(s => s.Id == id)] = slot; else _slots.Add(slot);
        foreach (var clash in conflicts.Where(c => c.SlotId is not null)) // the lessons it clashes with carry the badge too, so both cards show it
        {
            var i = _slots.FindIndex(x => x.Id == clash.SlotId);
            if (i >= 0 && !_slots[i].Badges.Contains(SlotBadges.Conflict)) _slots[i] = _slots[i] with { Badges = [.. _slots[i].Badges, SlotBadges.Conflict] };
        }

        return new SaveSlotResultDto(slot, conflicts);
    }

    private List<SlotConflictDto> ConflictsOf(SlotDto slot, Guid? ignore)
    {
        var list = new List<SlotConflictDto>();
        foreach (var s in _slots.Where(s => s.Id != ignore && s.Id != slot.Id && s.DayOfWeek == slot.DayOfWeek && s.PeriodId == slot.PeriodId))
            list.Add(new SlotConflictDto(ConflictKinds.ClassDoubleBooked, s.Id, s.CourseNameAr, s.CourseNameEn, s.TeacherName));
        foreach (var s in _otherClasses.Where(s => s.DayOfWeek == slot.DayOfWeek && s.PeriodId == slot.PeriodId && s.TeacherUserId == slot.TeacherUserId))
            list.Add(new SlotConflictDto(ConflictKinds.TeacherDoubleBooked, s.Id, s.CourseNameAr, s.CourseNameEn, s.TeacherName));
        return list;
    }

    private ClassTimetableDto Snapshot() => new(ClassId, TermId, WorkingDays, _periods.OrderBy(p => p.Order).ToList(), _slots.ToList(), ConflictMode,
        _slots.Count(s => s.State == SlotStates.Draft), DateTime.UtcNow);

    private SlotDto Slot(Guid course, string ar, string en, Guid teacher, string teacherName, DayOfWeek day, int period, string? room, string state) =>
        new(Guid.NewGuid(), ClassId, course, TermId, ar, en, teacher, teacherName, day, PeriodId(period), room, state, 1, []);

    private static (string Ar, string En) CourseName(Guid course) =>
        course == Maths ? ("الرياضيات", "Mathematics") : course == Arabic ? ("اللغة العربية", "Arabic") : ("العلوم", "Science");

    private static string TeacherName(Guid teacher) => teacher == Saleh ? "A. Saleh" : teacher == Nasser ? "M. Nasser" : "L. Omar";

    private void Validate(SavePeriodRequest request, Guid? ignore)
    {
        if (request.StartTime >= request.EndTime) throw Problem(400, "period_overlap");
        if (_periods.Any(p => p.Id != ignore && request.StartTime < p.EndTime && p.StartTime < request.EndTime)) throw Problem(400, "period_overlap");
    }

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);
}
