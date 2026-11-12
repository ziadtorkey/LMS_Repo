using System.Net.Http.Headers;
using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Imports;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Course import (M3 Task 6): every route's first gate is the definition's permission (<c>Courses.Import</c>, held by
/// the Org Admin only), then the batch's target unit. Columns and template are client-agnostic; a batch of a sibling
/// school or another client is 404. Batches are the unchanged template uploaded at a fresh unit below School1: no data
/// rows, so validation ends <c>Invalid</c> (an error report exists) and commit answers 409 <c>import_not_valid</c> to
/// anyone allowed to reach it.
/// </summary>
public sealed class CourseImportsProbeRows : IProbeRowModule
{
    private const string Kind = ImportKinds.Courses;

    public IEnumerable<ProbeRow> Rows =>
    [
        new("imports columns (courses)", HttpMethod.Get, Routes.ImportColumnsPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportColumns(Kind))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports template (courses)", HttpMethod.Get, Routes.ImportTemplatePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportTemplate(Kind, "ar"))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports upload (courses, below School1)", HttpMethod.Post, Routes.ImportKindPattern,
            async (w, _) => new ProbeRequest(Routes.ImportsOfKind(Kind), await UploadBodyAsync(w, await w.NewUnitAsync(w.Callers.A.School1))),
            Expect(Accepted, Accepted, NotFound, Forbidden, NotFound, Accepted)),
        new("imports list (courses)", HttpMethod.Get, Routes.ImportKindPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportsOfKind(Kind))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports batch get (courses)", HttpMethod.Get, Routes.ImportBatchPattern,
            async (w, _) => new ProbeRequest(Routes.ImportBatch(Kind, await NewBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports batch rows (courses)", HttpMethod.Get, Routes.ImportRowsPattern,
            async (w, _) => new ProbeRequest(Routes.ImportRows(Kind, await NewBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports error report (courses)", HttpMethod.Get, Routes.ImportErrorReportPattern,
            async (w, _) => new ProbeRequest(Routes.ImportErrorReport(Kind, await NewBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports commit (courses, invalid batch)", HttpMethod.Post, Routes.ImportCommitPattern,
            async (w, _) => new ProbeRequest(Routes.ImportCommit(Kind, await NewBatchAsync(w))),
            Expect(Conflict, Conflict, NotFound, Forbidden, NotFound, Conflict)),
        new("imports discard (courses)", HttpMethod.Delete, Routes.ImportBatchPattern,
            async (w, _) => new ProbeRequest(Routes.ImportBatch(Kind, await NewBatchAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static async Task<Guid> NewBatchAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        var batch = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ImportsOfKind(Kind), await UploadBodyAsync(w, unit));
        var id = batch.GetProperty("id").GetGuid();

        // The validation job runs on the real Hangfire server: wait until the batch has its verdict (and report).
        for (var i = 0; i < 100; i++)
        {
            var state = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Get, Routes.ImportBatch(Kind, id));
            if (state.GetProperty("status").GetString() is not ("Uploaded" or "Validating"))
            {
                break;
            }

            await Task.Delay(100);
        }

        return id;
    }

    /// <summary>The courses template as downloaded (headers only), posted as the multipart <c>file</c> + <c>orgUnitId</c>.</summary>
    private static async Task<MultipartFormDataContent> UploadBodyAsync(M1ProbeWorld w, OrgUnit unit)
    {
        using var template = await w.Callers.SendAsync(RootAdmin, HttpMethod.Get, Routes.ImportTemplate(Kind, "en"));
        Assert.True(template.IsSuccessStatusCode, $"setup: courses template download failed ({(int)template.StatusCode})");
        var file = new ByteArrayContent(await template.Content.ReadAsByteArrayAsync());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent
        {
            { file, "file", "courses-probe.xlsx" },
            { new StringContent(unit.Id.ToString()), "orgUnitId" },
        };
    }
}
