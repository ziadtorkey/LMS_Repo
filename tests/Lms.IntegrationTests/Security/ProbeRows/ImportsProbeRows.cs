using System.Net.Http.Headers;
using System.Text.Json;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Imports;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Staff import (M2 Tasks 7, 17): every route's first gate is the definition's permission (<c>Staff.Import</c>, held by
/// the Org Admin only), then the batch's target unit. Columns and template are client-agnostic; a batch of a sibling
/// school or another client is 404. Batches are the unchanged template uploaded at a fresh unit below School1. The probe
/// world runs no background jobs, so those batches stay <c>Uploaded</c> and commit answers 409 <c>import_not_valid</c> to
/// anyone allowed to reach it. The error report only exists after validation, so that row reads one batch that was
/// validated for real (see <see cref="ValidatedBatchAsync"/>).
/// </summary>
public sealed class ImportsProbeRows : IProbeRowModule
{
    private const string Kind = ImportKinds.Staff;

    public IEnumerable<ProbeRow> Rows =>
    [
        new("imports columns (staff)", HttpMethod.Get, Routes.ImportColumnsPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportColumns(Kind))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports template (staff)", HttpMethod.Get, Routes.ImportTemplatePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportTemplate(Kind, "ar"))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports upload (staff, below School1)", HttpMethod.Post, Routes.ImportKindPattern,
            async (w, _) => new ProbeRequest(Routes.ImportsOfKind(Kind), await UploadBodyAsync(w, await w.NewUnitAsync(w.Callers.A.School1))),
            Expect(Accepted, Accepted, NotFound, Forbidden, NotFound, Accepted)),
        new("imports list (staff)", HttpMethod.Get, Routes.ImportKindPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ImportsOfKind(Kind))),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("imports batch get", HttpMethod.Get, Routes.ImportBatchPattern,
            async (w, _) => new ProbeRequest(Routes.ImportBatch(Kind, await NewBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports batch rows", HttpMethod.Get, Routes.ImportRowsPattern,
            async (w, _) => new ProbeRequest(Routes.ImportRows(Kind, await NewBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports error report", HttpMethod.Get, Routes.ImportErrorReportPattern,
            async (w, _) => new ProbeRequest(Routes.ImportErrorReport(Kind, await ValidatedBatchAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("imports commit (invalid batch)", HttpMethod.Post, Routes.ImportCommitPattern,
            async (w, _) => new ProbeRequest(Routes.ImportCommit(Kind, await NewBatchAsync(w))),
            Expect(Conflict, Conflict, NotFound, Forbidden, NotFound, Conflict)),
        new("imports discard", HttpMethod.Delete, Routes.ImportBatchPattern,
            async (w, _) => new ProbeRequest(Routes.ImportBatch(Kind, await NewBatchAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static readonly SemaphoreSlim ValidatedGate = new(1, 1);
    private static (LmsApiFactory Factory, Guid Id)? _validated;

    /// <summary>
    /// One batch that went through the real validation job (header-only template: <c>Invalid</c> with an error report), made
    /// once per run. It is built through a host with recorded jobs that this method runs inline; every caller then reads it
    /// from the shared host (same database, same file storage). The error-report route only reads, so sharing it is safe.
    /// </summary>
    private static async Task<Guid> ValidatedBatchAsync(M1ProbeWorld w)
    {
        await ValidatedGate.WaitAsync();
        try
        {
            if (_validated is { } cached && ReferenceEquals(cached.Factory, w.Callers.Factory))
            {
                return cached.Id;
            }

            var unit = await w.NewUnitAsync(w.Callers.A.School1);
            using var template = await w.Callers.SendAsync(RootAdmin, HttpMethod.Get, Routes.ImportTemplate(Kind, "en"));
            Assert.True(template.IsSuccessStatusCode, $"setup: staff template download failed ({(int)template.StatusCode})");

            using var host = new ImportTestHost(w.Callers.Factory, withTestDefinition: false);
            using var upload = await host.UploadAsync(
                w.Callers.AssignmentOf(RootAdmin), unit.Id, await template.Content.ReadAsByteArrayAsync(), "staff-probe.xlsx", kind: Kind);
            var text = await upload.Content.ReadAsStringAsync();
            Assert.True(upload.IsSuccessStatusCode, $"setup: upload failed ({(int)upload.StatusCode}): {text}");
            var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();

            await host.RunQueuedJobsAsync();
            var state = await host.BatchAsync(w.Callers.Factory, id);
            Assert.True(state.ErrorReportFileId is not null, $"setup: the validated batch has no error report (status {state.Status}, {state.ErrorCount} errors)");

            _validated = (w.Callers.Factory, id);
            return id;
        }
        finally
        {
            ValidatedGate.Release();
        }
    }

    private static async Task<Guid> NewBatchAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        var batch = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ImportsOfKind(Kind), await UploadBodyAsync(w, unit));
        return batch.GetProperty("id").GetGuid();
    }

    /// <summary>The staff template as downloaded (headers only), posted as the multipart <c>file</c> + <c>orgUnitId</c>.</summary>
    private static async Task<MultipartFormDataContent> UploadBodyAsync(M1ProbeWorld w, OrgUnit unit)
    {
        using var template = await w.Callers.SendAsync(RootAdmin, HttpMethod.Get, Routes.ImportTemplate(Kind, "en"));
        Assert.True(template.IsSuccessStatusCode, $"setup: staff template download failed ({(int)template.StatusCode})");
        var file = new ByteArrayContent(await template.Content.ReadAsByteArrayAsync());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        return new MultipartFormDataContent
        {
            { file, "file", "staff-probe.xlsx" },
            { new StringContent(unit.Id.ToString()), "orgUnitId" },
        };
    }
}
