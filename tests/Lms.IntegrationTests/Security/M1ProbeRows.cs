using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using static Lms.IntegrationTests.Security.Caller;

namespace Lms.IntegrationTests.Security;

/// <summary>The concrete request one probe sends.</summary>
public sealed record ProbeRequest(string Url, object? Body = null);

/// <summary>
/// One row of the M1 probe matrix: an endpoint (method + route pattern exactly as mapped), how to build a request for a
/// caller (fresh targets are created here, as the root admin), and the expected status for every caller.
/// </summary>
public sealed record ProbeRow(
    string Name,
    HttpMethod Method,
    string Pattern,
    Func<M1ProbeWorld, Caller, Task<ProbeRequest>> Build,
    IReadOnlyDictionary<Caller, int> Expected);

/// <summary>
/// Every M1 endpoint with the expected status per caller (M1 plan Task 35; Review Focus 1, 4). Rows whose route is not
/// mapped yet are reported as pending and become live as soon as the feature merges.
/// Callers: RootAdmin (A root), School1Admin, School2Admin (sibling), School1Teacher, ClientBAdmin, PlatformAdmin (no
/// context), Anonymous. Targets live in client A at or below School1 unless the row says otherwise.
/// </summary>
public static class M1ProbeRows
{
    private const int Ok = 200, Created = 201, NoContent = 204, BadRequest = 400, Unauthorized = 401, Forbidden = 403,
        NotFound = 404;

    /// <summary>Expected status per caller, in the order of <see cref="Caller"/>.</summary>
    private static Dictionary<Caller, int> Expect(int root, int school1, int school2, int teacher, int clientB, int platform,
        int anonymous = Unauthorized) => new()
    {
        [RootAdmin] = root, [School1Admin] = school1, [School2Admin] = school2, [School1Teacher] = teacher,
        [ClientBAdmin] = clientB, [PlatformAdmin] = platform, [Anonymous] = anonymous,
    };

    public static IReadOnlyList<ProbeRow> All { get; } =
    [
        // ---- Level types (Task 8, #83): client-wide. Reads at any scope; writes only at the client root (Review Focus 4).
        new("level-types list", HttpMethod.Get, Routes.LevelTypes,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LevelTypes)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Forbidden)),
        new("level-types create", HttpMethod.Post, Routes.LevelTypes,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.LevelTypes, LevelTypeBody(w))),
            Expect(Created, Forbidden, Forbidden, Forbidden, Created, Forbidden)),
        new("level-types update (client A type)", HttpMethod.Put, Routes.LevelTypePattern,
            async (w, _) => new ProbeRequest(Routes.LevelType(await NewLevelTypeAsync(w)), LevelTypeBody(w)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("level-types reorder (client A ids)", HttpMethod.Put, Routes.LevelTypesOrder,
            async (w, _) => new ProbeRequest(Routes.LevelTypesOrder, new ReorderLevelTypesRequest(await LevelTypeIdsOfAAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, BadRequest, Forbidden)),
        new("level-types delete (client A type)", HttpMethod.Delete, Routes.LevelTypePattern,
            async (w, _) => new ProbeRequest(Routes.LevelType(await NewLevelTypeAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),

        // ---- Academic years (Task 20, #102): unit-targeted, target unit below School1 (Review Focus 1: sibling/other client 404).
        new("academic-years effective", HttpMethod.Get, Routes.AcademicYearsEffective,
            async (w, _) => new ProbeRequest($"{Routes.AcademicYearsEffective}?orgUnitId={(await w.NewUnitAsync(w.Callers.A.School1)).Id}"),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("academic-years create", HttpMethod.Post, Routes.AcademicYears,
            async (w, _) => new ProbeRequest(Routes.AcademicYears, YearBody(w, (await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("academic-years update", HttpMethod.Put, Routes.AcademicYearPattern,
            async (w, _) =>
            {
                var (yearId, unitId) = await NewYearAsync(w);
                return new ProbeRequest(Routes.AcademicYear(yearId), YearBody(w, unitId));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("academic-years activate", HttpMethod.Post, Routes.AcademicYearActivatePattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYearActivate((await NewYearAsync(w)).YearId)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("academic-years delete (Planned)", HttpMethod.Delete, Routes.AcademicYearPattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYear((await NewYearAsync(w)).YearId)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),

        // ---- Terms (Task 20, #102): checked at the year's unit.
        new("terms create", HttpMethod.Post, Routes.AcademicYearTermsPattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYearTerms((await NewYearAsync(w)).YearId), TermBody()),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("terms update", HttpMethod.Put, Routes.TermPattern,
            async (w, _) => new ProbeRequest(Routes.Term(await NewTermAsync(w)), TermBody()),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("terms delete", HttpMethod.Delete, Routes.TermPattern,
            async (w, _) => new ProbeRequest(Routes.Term(await NewTermAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),

        // ---- Academic calendar (Task 22, #115): events at the year's unit; checked at the event's unit.
        new("calendar read", HttpMethod.Get, Routes.AcademicYearCalendarPattern,
            async (w, _) =>
            {
                var (yearId, unitId) = await NewYearAsync(w);
                return new ProbeRequest($"{Routes.AcademicYearCalendar(yearId)}?orgUnitId={unitId}");
            },
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("calendar-events create", HttpMethod.Post, Routes.CalendarEvents,
            async (w, _) =>
            {
                var (yearId, unitId) = await NewYearAsync(w);
                return new ProbeRequest(Routes.CalendarEvents, EventBody(yearId, unitId));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("calendar-events update", HttpMethod.Put, Routes.CalendarEventPattern,
            async (w, _) =>
            {
                var (eventId, yearId, unitId) = await NewEventAsync(w);
                return new ProbeRequest(Routes.CalendarEvent(eventId), EventBody(yearId, unitId));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("calendar-events delete", HttpMethod.Delete, Routes.CalendarEventPattern,
            async (w, _) => new ProbeRequest(Routes.CalendarEvent((await NewEventAsync(w)).EventId)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),

        // ---- Settings (Task 14, #100): catalog is client-agnostic; unit settings are checked at the target unit.
        new("settings catalog", HttpMethod.Get, Routes.SettingsCatalog,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.SettingsCatalog)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("unit settings read", HttpMethod.Get, Routes.OrgUnitSettingsPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitSettings((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit setting set", HttpMethod.Put, Routes.OrgUnitSettingPattern,
            async (w, _) => new ProbeRequest(
                Routes.OrgUnitSetting((await w.NewUnitAsync(w.Callers.A.School1)).Id, ProbeSetting), new SetSettingRequest("true", false)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("unit setting remove", HttpMethod.Delete, Routes.OrgUnitSettingPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitSetting(await NewUnitWithSettingAsync(w), ProbeSetting)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),

        // ---- Org unit move (Task 11, #46): needs OrgUnits.Move at the unit AND at the new parent. Both are fresh units
        // below School1 (the unit one level deeper than its target), so every allowed caller really moves it.
        new("org unit move", HttpMethod.Put, Routes.OrgUnitMovePattern,
            async (w, _) =>
            {
                var (unitId, targetId) = await NewMovablePairAsync(w);
                return new ProbeRequest(Routes.OrgUnitMove(unitId), new MoveOrgUnitRequest(targetId));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),

        // ---- Roles (Task 19, #96): client-wide. Reads at any scope; writes only at the client root (D18, Review Focus 4);
        // a platform admin has no client, so client-root writes are refused; roles of another client are invisible (404).
        new("permissions catalog", HttpMethod.Get, Routes.Permissions,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Permissions)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("roles list", HttpMethod.Get, Routes.Roles,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Roles)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("roles create", HttpMethod.Post, Routes.Roles,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Roles, RoleBody(w))),
            Expect(Created, Forbidden, Forbidden, Forbidden, Created, Forbidden)),
        new("roles update (client A custom role)", HttpMethod.Put, Routes.RolePattern,
            async (w, _) => new ProbeRequest(Routes.Role(await NewRoleAsync(w)), RoleUpdateBody(w)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
        new("roles delete (unused client A custom role)", HttpMethod.Delete, Routes.RolePattern,
            async (w, _) => new ProbeRequest(Routes.Role(await NewRoleAsync(w))),
            Expect(NoContent, Forbidden, Forbidden, Forbidden, NotFound, Forbidden)),
    ];

    private static CreateRoleRequest RoleBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new CreateRoleRequest("دور " + n, "Probe role " + n, RoleBaseKinds.Staff, [Permissions.Roles.View]);
    }

    private static UpdateRoleRequest RoleUpdateBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new UpdateRoleRequest("دور " + n, "Probe role " + n, [Permissions.Roles.View]);
    }

    private static async Task<Guid> NewRoleAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Roles, RoleBody(w))).GetProperty("id").GetGuid();

    private const string ProbeSetting = "Ui.ShowHijriDates";

    private static async Task<Guid> NewUnitWithSettingAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Put, Routes.OrgUnitSetting(unit.Id, ProbeSetting), new SetSettingRequest("true", false));
        return unit.Id;
    }

    /// <summary>A unit and a target parent, both fresh below School1; the unit sits on a deeper level than the target.</summary>
    private static async Task<(Guid UnitId, Guid TargetId)> NewMovablePairAsync(M1ProbeWorld w)
    {
        var target = await w.NewUnitAsync(w.Callers.A.School1);
        var n = w.Next();
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var deeper = new LevelType(target.OrganizationId, "مستوى أعمق " + n, "Deeper probe level " + n, 1000 + n, true);
        var unit = OrgUnit.CreateChild(w.Callers.A.School1, deeper.Id, "وحدة للنقل " + n, "Movable probe unit " + n, "M" + n);
        db.AddRange(deeper, unit);
        await db.SaveChangesAsync();
        return (unit.Id, target.Id);
    }

    // ---- Target builders (always created as client A's root admin).

    private static SaveLevelTypeRequest LevelTypeBody(M1ProbeWorld w)
    {
        var n = w.Next();
        return new SaveLevelTypeRequest("مستوى " + n, "Probe level " + n, false);
    }

    private static async Task<Guid> NewLevelTypeAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.LevelTypes, LevelTypeBody(w))).GetProperty("id").GetGuid();

    private static async Task<IReadOnlyList<Guid>> LevelTypeIdsOfAAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Get, Routes.LevelTypes)).EnumerateArray()
        .OrderBy(t => t.GetProperty("order").GetInt32())
        .Select(t => t.GetProperty("id").GetGuid())
        .ToList();

    private static readonly DateOnly YearStart = new(2040, 9, 1);

    private static SaveAcademicYearRequest YearBody(M1ProbeWorld w, Guid unitId)
    {
        var n = w.Next();
        return new SaveAcademicYearRequest(unitId, "عام " + n, "Probe year " + n, YearStart, YearStart.AddMonths(10));
    }

    private static async Task<(Guid YearId, Guid UnitId)> NewYearAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        var year = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.AcademicYears, YearBody(w, unit.Id));
        return (year.GetProperty("id").GetGuid(), unit.Id);
    }

    private static SaveTermRequest TermBody() => new("فصل", "Probe term", YearStart, YearStart.AddMonths(4));

    private static async Task<Guid> NewTermAsync(M1ProbeWorld w)
    {
        var (yearId, _) = await NewYearAsync(w);
        var term = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.AcademicYearTerms(yearId), TermBody());
        return term.GetProperty("id").GetGuid();
    }

    private static SaveCalendarEventRequest EventBody(Guid yearId, Guid unitId) =>
        new(yearId, unitId, "عطلة", "Probe holiday", CalendarEventKinds.Holiday, YearStart.AddDays(30), YearStart.AddDays(31));

    private static async Task<(Guid EventId, Guid YearId, Guid UnitId)> NewEventAsync(M1ProbeWorld w)
    {
        var (yearId, unitId) = await NewYearAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.CalendarEvents, EventBody(yearId, unitId));
        return (created.GetProperty("id").GetGuid(), yearId, unitId);
    }
}
