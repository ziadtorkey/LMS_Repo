using Lms.Shared;
using Lms.Shared.AcademicYears;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Academic years and terms (Task 20, #102): unit-targeted, target unit below School1 (Review Focus 1: sibling/other
/// client 404); terms are checked at the year's unit. The year builders are shared with <see cref="CalendarProbeRows"/>.
/// </summary>
public sealed class AcademicYearsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
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

        new("terms create", HttpMethod.Post, Routes.AcademicYearTermsPattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYearTerms((await NewYearAsync(w)).YearId), TermBody()),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("terms update", HttpMethod.Put, Routes.TermPattern,
            async (w, _) => new ProbeRequest(Routes.Term(await NewTermAsync(w)), TermBody()),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("terms delete", HttpMethod.Delete, Routes.TermPattern,
            async (w, _) => new ProbeRequest(Routes.Term(await NewTermAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),

        // The term structure of a year (M5 Task 5, L8): read with AcademicYears.View, applied with AcademicYears.Update.
        new("term structure get", HttpMethod.Get, Routes.AcademicYearTermStructurePattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYearTermStructure((await NewYearAsync(w)).YearId)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("term structure apply", HttpMethod.Put, Routes.AcademicYearTermStructurePattern,
            async (w, _) => new ProbeRequest(Routes.AcademicYearTermStructure((await NewYearAsync(w)).YearId), new TermStructureDto(2, [])),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    internal static readonly DateOnly YearStart = new(2040, 9, 1);

    private static SaveAcademicYearRequest YearBody(M1ProbeWorld w, Guid unitId)
    {
        var n = w.Next();
        return new SaveAcademicYearRequest(unitId, "عام " + n, "Probe year " + n, YearStart, YearStart.AddMonths(10));
    }

    /// <summary>A fresh Planned year on a fresh unit below School1, created as the root admin.</summary>
    internal static async Task<(Guid YearId, Guid UnitId)> NewYearAsync(M1ProbeWorld w)
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
}
