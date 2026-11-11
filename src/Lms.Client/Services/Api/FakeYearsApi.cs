using Lms.Shared.AcademicYears;

namespace Lms.Client.Services.Api;

/// <summary>In-memory years/terms/calendar for the standalone dev server and bUnit tests.</summary>
public sealed class FakeYearsApi : IYearsApi
{
    private sealed class Year
    {
        public Guid Id = Guid.NewGuid();
        public Guid UnitId;
        public string NameAr = "", NameEn = "", Status = AcademicYearStatuses.Planned;
        public DateOnly Start, End;
        public List<TermDto> Terms = [];
    }

    private readonly List<Year> _years = [];
    private readonly List<CalendarEventDto> _events = [];

    /// <summary>When set, the next mutating call throws it (tests simulate server problems).</summary>
    public Exception? NextFailure { get; set; }
    /// <summary>When set, every effective-years read throws it (tests simulate a forbidden or offline API).</summary>
    public Exception? EffectiveFailure { get; set; }
    public SaveAcademicYearRequest? LastYear { get; private set; }
    public SaveTermRequest? LastTerm { get; private set; }
    public SaveCalendarEventRequest? LastEvent { get; private set; }

    public FakeYearsApi()
    {
        // The group defines 2026/2027 (active, two terms); schools and grades inherit it.
        var year = AddYear(FakeUnits.GroupId, "2026/2027", "2026/2027", new(2026, 9, 1), new(2027, 6, 30), AcademicYearStatuses.Active);
        AddTerm(year.Id, "الفصل الأول", "Term 1", new(2026, 9, 1), new(2027, 1, 20));
        AddTerm(year.Id, "الفصل الثاني", "Term 2", new(2027, 2, 1), new(2027, 6, 30));
        AddEvent(year.Id, FakeUnits.GroupId, "عطلة اليوم الوطني", "National Day", CalendarEventKinds.Holiday, new(2026, 9, 23), new(2026, 9, 23));
    }

    public Guid YearIdAt(Guid unitId, string status) => _years.First(y => y.UnitId == unitId && y.Status == status).Id;

    /// <summary>Adds a year at a unit (tests and demo data).</summary>
    public AcademicYearDto AddYear(Guid unitId, string ar, string en, DateOnly start, DateOnly end, string status)
    {
        var y = new Year { UnitId = unitId, NameAr = ar, NameEn = en, Start = start, End = end, Status = status };
        _years.Add(y);
        return ToDto(y, unitId);
    }

    public TermDto AddTerm(Guid yearId, string ar, string en, DateOnly start, DateOnly end)
    {
        var y = _years.First(x => x.Id == yearId);
        y.Terms.Add(new TermDto(Guid.NewGuid(), ar, en, 0, start, end, IsToday(start, end)));
        y.Terms = y.Terms.OrderBy(t => t.StartDate).Select((t, i) => t with { Order = i + 1 }).ToList();
        return y.Terms.First(t => t.StartDate == start);
    }

    public CalendarEventDto AddEvent(Guid yearId, Guid unitId, string ar, string en, string kind, DateOnly start, DateOnly end)
    {
        var unit = Lms.Client.Services.Api.FakeUnits.All.First(u => u.Id == unitId);
        var e = new CalendarEventDto(Guid.NewGuid(), yearId, unitId, unit.NameAr, unit.NameEn, ar, en, kind, start, end, false);
        _events.Add(e);
        return e;
    }

    public Task<YearContextDto> GetEffectiveAsync(Guid? orgUnitId, CancellationToken ct = default)
    {
        if (EffectiveFailure is { } failure) throw failure;
        var unitId = orgUnitId ?? FakeUnits.GroupId;
        for (var u = FakeUnits.All.FirstOrDefault(x => x.Id == unitId); u is not null;
             u = u.ParentId is { } p ? FakeUnits.All.First(x => x.Id == p) : null)
        {
            var years = _years.Where(y => y.UnitId == u.Id).OrderByDescending(y => y.Start).Select(y => ToDto(y, unitId)).ToList();
            if (years.Count > 0)
                return Task.FromResult(new YearContextDto(years, years.FirstOrDefault(y => y.Status == AcademicYearStatuses.Active)?.Id, u.Id));
        }
        return Task.FromResult(new YearContextDto([], null, unitId));
    }

    public Task<AcademicYearDto> CreateYearAsync(SaveAcademicYearRequest request, CancellationToken ct = default)
    {
        Fail();
        LastYear = request;
        return Task.FromResult(AddYear(request.OrgUnitId, request.NameAr, request.NameEn, request.StartDate, request.EndDate, AcademicYearStatuses.Planned));
    }

    public Task<AcademicYearDto> UpdateYearAsync(Guid id, SaveAcademicYearRequest request, CancellationToken ct = default)
    {
        Fail();
        LastYear = request;
        var y = _years.First(x => x.Id == id);
        (y.NameAr, y.NameEn, y.Start, y.End) = (request.NameAr, request.NameEn, request.StartDate, request.EndDate);
        return Task.FromResult(ToDto(y, y.UnitId));
    }

    public Task ActivateYearAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        var y = _years.First(x => x.Id == id);
        if (_years.Any(o => o.UnitId == y.UnitId && o.Status == AcademicYearStatuses.Active))
            throw new ApiProblemException(409, "active_year_exists", null, null);
        y.Status = AcademicYearStatuses.Active;
        return Task.CompletedTask;
    }

    public Task DeleteYearAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        var y = _years.First(x => x.Id == id);
        if (y.Status != AcademicYearStatuses.Planned)
            throw new ApiProblemException(409, y.Status == AcademicYearStatuses.Closed ? "year_closed" : "year_active", null, null);
        _years.Remove(y);
        return Task.CompletedTask;
    }

    public Task<TermDto> CreateTermAsync(Guid yearId, SaveTermRequest request, CancellationToken ct = default)
    {
        Fail();
        LastTerm = request;
        var y = _years.First(x => x.Id == yearId);
        if (request.StartDate < y.Start || request.EndDate > y.End)
            throw new ApiProblemException(409, "term_outside_year", null, null);
        return Task.FromResult(AddTerm(yearId, request.NameAr, request.NameEn, request.StartDate, request.EndDate));
    }

    public Task<TermDto> UpdateTermAsync(Guid id, SaveTermRequest request, CancellationToken ct = default)
    {
        Fail();
        LastTerm = request;
        var y = _years.First(x => x.Terms.Any(t => t.Id == id));
        y.Terms = y.Terms.Select(t => t.Id == id
            ? t with { NameAr = request.NameAr, NameEn = request.NameEn, StartDate = request.StartDate, EndDate = request.EndDate, IsCurrent = IsToday(request.StartDate, request.EndDate) }
            : t).ToList();
        return Task.FromResult(y.Terms.First(t => t.Id == id));
    }

    public Task DeleteTermAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        foreach (var y in _years) y.Terms = y.Terms.Where(t => t.Id != id).ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(Guid yearId, Guid orgUnitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CalendarEventDto>>(_events.Where(e => e.AcademicYearId == yearId)
            .Select(e => e with { IsInherited = e.OrgUnitId != orgUnitId }).OrderBy(e => e.StartDate).ToList());

    public Task<CalendarEventDto> CreateEventAsync(SaveCalendarEventRequest request, CancellationToken ct = default)
    {
        Fail();
        LastEvent = request;
        return Task.FromResult(AddEvent(request.AcademicYearId, request.OrgUnitId, request.TitleAr, request.TitleEn,
            request.Kind, request.StartDate, request.EndDate));
    }

    public Task<CalendarEventDto> UpdateEventAsync(Guid id, SaveCalendarEventRequest request, CancellationToken ct = default)
    {
        Fail();
        LastEvent = request;
        var i = _events.FindIndex(e => e.Id == id);
        _events[i] = _events[i] with { TitleAr = request.TitleAr, TitleEn = request.TitleEn, Kind = request.Kind, StartDate = request.StartDate, EndDate = request.EndDate };
        return Task.FromResult(_events[i]);
    }

    public Task DeleteEventAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        _events.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    private AcademicYearDto ToDto(Year y, Guid viewedUnit)
    {
        var unit = FakeUnits.All.First(u => u.Id == y.UnitId);
        return new AcademicYearDto(y.Id, y.UnitId, unit.NameAr, unit.NameEn, y.NameAr, y.NameEn, y.Start, y.End, y.Status,
            y.UnitId != viewedUnit, y.Terms);
    }

    private static bool IsToday(DateOnly start, DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return start <= today && today <= end;
    }

    private void Fail()
    {
        if (NextFailure is not { } ex) return;
        NextFailure = null;
        throw ex;
    }
}
