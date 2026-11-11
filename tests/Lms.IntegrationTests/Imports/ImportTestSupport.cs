using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using ClosedXML.Excel;
using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Imports;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Imports;

/// <summary>
/// Import kind used by the framework tests: widgets with a code, a name, an optional grade, start date and unit code.
/// Commit creates one child unit per row (so rollback is visible) and queues one "email" per row after the commit.
/// </summary>
public sealed class TestImportDefinition(LmsDbContext db) : IImportDefinition
{
    public const string KindName = "test-widgets";

    /// <summary>Codes that count as already existing (a duplicate created by someone else after the preview).</summary>
    public static readonly ConcurrentDictionary<string, bool> Taken = new();

    /// <summary>"Emails" sent after the commit, by code.</summary>
    public static readonly ConcurrentQueue<string> Sent = new();

    public string Kind => KindName;
    public string Permission => Permissions.OrgUnits.Update;

    public IReadOnlyList<ImportColumn> Columns { get; } =
    [
        new("code", "الرمز", "Code", true, AsText: true, ExampleValue: "W-001"),
        new("name", "الاسم", "Name", true),
        new("grade", "الدرجة", "Grade", false, ["A", "B"]),
        new("start", "تاريخ البدء", "Start date", false, ExampleValue: "2026-09-01"),
        new("unit", "رمز الوحدة", "Unit code", false),
    ];

    public Task<IReadOnlyList<RowError>> ValidateAsync(
        ImportValidationContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken)
    {
        var errors = new List<RowError>();
        errors.AddRange(ImportChecks.DuplicatesInFile(rows, "code", v => v?.Trim().ToUpperInvariant()));
        foreach (var row in rows)
        {
            errors.AddRange(new[]
            {
                ImportChecks.Date(row, "start"), ImportChecks.UnitCode(row, "unit", context),
                row.Get("code") is { } code && Taken.ContainsKey(code.ToUpperInvariant())
                    ? new RowError(row.RowNumber, "code", "duplicate_in_file", new Dictionary<string, string> { ["otherRow"] = "db" })
                    : null,
            }.OfType<RowError>());
        }

        return Task.FromResult<IReadOnlyList<RowError>>(errors);
    }

    public async Task<int> CommitAsync(
        ImportCommitContext context, IReadOnlyList<ImportRowValues> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            var code = row.Get("code")!;
            db.Add(OrgUnit.CreateChild(context.TargetUnit, context.TargetUnit.LevelTypeId, row.Get("name")!, row.Get("name")!, code));
            await db.SaveChangesAsync(cancellationToken);
            if (code.StartsWith("BOOM", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("boom");
            }

            context.AfterCommit.Add(_ =>
            {
                Sent.Enqueue(code);
                return Task.CompletedTask;
            });
        }

        return rows.Count;
    }
}

/// <summary>Records enqueued jobs instead of handing them to Hangfire, so tests run them deterministically.</summary>
public sealed class RecordingBackgroundJobs(ICurrentContext current) : IBackgroundJobs
{
    public static readonly ConcurrentQueue<(JobContextSnapshot Context, Delegate Job, Type Type)> Queue = new();

    public string Enqueue<T>(Expression<Func<T, Task>> job) => Enqueue(JobContextSnapshot.From(current), job);

    public string Enqueue<T>(JobContextSnapshot context, Expression<Func<T, Task>> job)
    {
        Queue.Enqueue((context, job.Compile(), typeof(T)));
        return Guid.NewGuid().ToString("N");
    }

    public string EnqueueAsSystem<T>(Expression<Func<T, Task>> job) => Enqueue(JobContextSnapshot.System, job);

    public void AddOrUpdateRecurring<T>(string recurringJobId, Expression<Func<T, Task>> job, string cronExpression)
    {
    }
}

/// <summary>An API host with the test import kind, no Hangfire server and recorded jobs.</summary>
public sealed class ImportTestHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _host;

    public ImportTestHost(LmsApiFactory factory, bool withTestDefinition = true, Action<IServiceCollection>? configure = null)
    {
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jobs:ServerEnabled", "false");
            builder.ConfigureServices(services =>
            {
                if (withTestDefinition)
                {
                    services.AddImportDefinition<TestImportDefinition>();
                }

                services.RemoveAll<Lms.Infrastructure.Configuration.IPolicyEvaluator>(); // the real evaluator (policies apply to import rows)
                services.AddScoped<Lms.Infrastructure.Configuration.IPolicyEvaluator, Lms.Infrastructure.Configuration.PolicyEvaluator>();
                configure?.Invoke(services);
                services.RemoveAll<IBackgroundJobs>();
                services.AddScoped<IBackgroundJobs, RecordingBackgroundJobs>();
            });
        });
    }

    public IServiceProvider Services => _host.Services;

    public void Dispose() => _host.Dispose();

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment caller, HttpContent? content = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return _host.CreateClient().SendAsync(request);
    }

    public Task<HttpResponseMessage> UploadAsync(RoleAssignment caller, Guid orgUnitId, byte[] file, string fileName = "widgets.xlsx", string? language = null, string kind = TestImportDefinition.KindName)
    {
        var form = new MultipartFormDataContent { { new StringContent(orgUnitId.ToString()), "orgUnitId" } };
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(part, "file", fileName);
        return SendAsync(HttpMethod.Post, "/api/imports/" + kind, caller, form, language);
    }

    /// <summary>Runs every queued job as the context that enqueued it (what the Hangfire activator does).</summary>
    public async Task RunQueuedJobsAsync()
    {
        while (RecordingBackgroundJobs.Queue.TryDequeue(out var queued))
        {
            using var scope = _host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HttpCurrentContext>();
            var snapshot = queued.Context;
            context.SetUser(snapshot.UserId ?? Guid.Empty, snapshot.IsPlatformAdmin);
            if (snapshot is { AssignmentId: { } assignmentId, OrganizationId: { } organizationId, ScopePath: { } path })
            {
                context.SetAssignment(assignmentId, organizationId, path);
            }

            var job = ActivatorUtilities.CreateInstance(scope.ServiceProvider, queued.Type);
            await (Task)queued.Job.DynamicInvoke(job)!;
        }
    }

    public async Task<ImportBatchState> BatchAsync(LmsApiFactory factory, Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var batch = await db.Set<Lms.Domain.Imports.ImportBatch>().SingleAsync(b => b.Id == id);
        return new ImportBatchState(batch.Status.ToString(), batch.RowCount, batch.ErrorCount, batch.ErrorReportFileId, batch.CreatedCount);
    }
}

public sealed record ImportBatchState(string Status, int RowCount, int ErrorCount, Guid? ErrorReportFileId, int? CreatedCount);

public static class XlsxBuilder
{
    public static byte[] Create(string[] headers, params string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c] is { } text)
                {
                    sheet.Cell(r + 2, c + 1).Value = text;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static readonly string[] WidgetHeaders = ["Code", "Name", "Grade", "Start date", "Unit code"];
}
