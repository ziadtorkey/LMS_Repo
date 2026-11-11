using Lms.IntegrationTests.Academic;
using Lms.Shared;
using Lms.Shared.Classes;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Classes (M3 Task 13): written and read at the class unit. A School1 class is invisible to School2's admin and to
/// client B (404); a Teacher may list classes but reaches only those they teach (so a class they do not teach is 404)
/// and holds no write permission (403).
/// </summary>
public sealed class ClassesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("classes list", HttpMethod.Get, Routes.Classes,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Classes)),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("class get (School1)", HttpMethod.Get, Routes.ClassPattern,
            async (w, _) => new ProbeRequest(Routes.Class(await NewClassAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class create (School1)", HttpMethod.Post, Routes.Classes,
            async (w, _) => new ProbeRequest(Routes.Classes, (await BodyAsync(w))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("class update (School1)", HttpMethod.Put, Routes.ClassPattern,
            async (w, _) =>
            {
                var body = await BodyAsync(w);
                var id = await NewClassAsync(w, body);
                return new ProbeRequest(Routes.Class(id), body with { NameEn = "Updated" });
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class close (School1)", HttpMethod.Post, Routes.ClassClosePattern,
            async (w, _) => new ProbeRequest(Routes.ClassClose(await NewClassAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("class delete (School1, empty)", HttpMethod.Delete, Routes.ClassPattern,
            async (w, _) => new ProbeRequest(Routes.Class(await NewClassAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("classes bulk (School1)", HttpMethod.Post, Routes.ClassesBulk,
            async (w, _) =>
            {
                var n = w.Next();
                var year = await YearAsync(w);
                return new ProbeRequest(Routes.ClassesBulk, new BulkCreateClassesRequest(
                    w.Callers.A.School1.Id, year, "صف {N}", "Probe {N}", $"PB{n}-{{N}}", 1, 2, 20));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("classes copy (School1)", HttpMethod.Post, Routes.ClassesCopy,
            async (w, _) =>
            {
                var source = await YearAsync(w);
                var body = await BodyAsync(w, source);
                await NewClassAsync(w, body);
                return new ProbeRequest(Routes.ClassesCopy, new CopyClassesRequest(
                    source, await YearAsync(w), w.Callers.A.School1.Id, false, null, null));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static async Task<Guid> YearAsync(M1ProbeWorld w) =>
        (await AcademicTestKit.AddYearAsync(w.Callers.Factory, w.Callers.A.School1)).Id;

    private static async Task<SaveClassRequest> BodyAsync(M1ProbeWorld w, Guid? year = null) =>
        new("PR" + w.Next() + Guid.NewGuid().ToString("N")[..6], "فصل", "Probe class", w.Callers.A.School1.Id,
            year ?? await YearAsync(w), 20, null);

    private static async Task<Guid> NewClassAsync(M1ProbeWorld w, SaveClassRequest? body = null) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Classes, body ?? await BodyAsync(w))).GetProperty("id").GetGuid();
}
