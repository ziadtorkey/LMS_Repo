using System.Net.Http.Headers;
using Lms.Api.Features.Students.Import;
using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Imports;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Student import (M3 Task 23, #231): the generic <c>/api/imports/{kind}</c> rows of <see cref="ImportsProbeRows"/> cover the
/// framework routes of every kind; this adds the students-only credentials sheet. It needs Students.Import (Org Admin only) at the
/// batch's unit and a committed batch of the caller, so an uncommitted probe batch is 404 for everyone allowed to look (the
/// download itself is covered by StudentImportTests); a Teacher is stopped at the permission.
/// </summary>
public sealed class StudentImportProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("imports credentials sheet (students, not committed)", HttpMethod.Get, "/api/imports/{kind}/{id:guid}/credentials",
            async (w, _) => new ProbeRequest(CredentialsSheetEndpoint.Pattern.Replace("{id:guid}", (await NewBatchAsync(w)).ToString())),
            Expect(NotFound, NotFound, NotFound, Forbidden, NotFound, NotFound)),
    ];

    private static async Task<Guid> NewBatchAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        using var template = await w.Callers.SendAsync(RootAdmin, HttpMethod.Get, Routes.ImportTemplate(ImportKinds.Students, "en"));
        Assert.True(template.IsSuccessStatusCode, $"setup: student template download failed ({(int)template.StatusCode})");
        var file = new ByteArrayContent(await template.Content.ReadAsByteArrayAsync());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var body = new MultipartFormDataContent { { file, "file", "students-probe.xlsx" }, { new StringContent(unit.Id.ToString()), "orgUnitId" } };
        var batch = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ImportsOfKind(ImportKinds.Students), body);
        var id = batch.GetProperty("id").GetGuid();
        await ImportsProbeRows.WaitForValidationAsync(w, ImportKinds.Students, id);
        return id;
    }
}
