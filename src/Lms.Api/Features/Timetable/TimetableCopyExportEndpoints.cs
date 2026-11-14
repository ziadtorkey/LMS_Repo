using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Timetable;

/// <summary>Copy a class timetable between terms and export it to Excel (FR-TT-04). Same rules as the builder: unit access decides, a miss is 404.</summary>
public static class TimetableCopyExportEndpoints
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapTimetableCopyExport(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassTimetableCopyTermPattern, CopyTermAsync).RequirePermission(Permissions.Timetable.Manage);
        app.MapGet(Routes.ClassTimetableExportPattern, ExportAsync).RequirePermission(Permissions.Timetable.View);
        return app;
    }

    private static async Task<IResult> CopyTermAsync(
        Guid id, CopyTermRequest? body, HttpContext http, LmsDbContext db, TimetableService timetable, PeriodService periods, CopyTermService copy, CancellationToken ct)
    {
        var @class = await timetable.FindClassAsync(id, ct);
        if (@class is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Timetable.Manage, @class.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        // Both terms must be terms of this class academic year in this client; anything else (another year, school or client) is a 404.
        if (body is null || await timetable.FindTermAsync(@class, body.SourceTermId, ct) is null || await timetable.FindTermAsync(@class, body.TargetTermId, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        if (body.SourceTermId == body.TargetTermId)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The source and target terms must differ.");
        }

        var unit = await db.OrgUnits.SingleAsync(u => u.Id == @class.OrgUnitId, ct);
        var (periodRows, _) = await periods.EffectiveAsync(unit, ct);
        return Results.Ok(await copy.CopyAsync(@class, body.SourceTermId, body.TargetTermId, periodRows.Select(p => p.Id).ToHashSet(), ct));
    }

    private static async Task<IResult> ExportAsync(
        Guid id, Guid? termId, HttpContext http, LmsDbContext db, TimetableService timetable, PeriodService periods, ISettingsResolver settings,
        IAccessEvaluator access, ICurrentContext context, IAuditWriter audit, CancellationToken ct)
    {
        var @class = await timetable.FindClassAsync(id, ct);
        if (@class is null)
        {
            return ApiProblems.NotFound();
        }

        var check = await http.CheckUnitAsync(Permissions.Timetable.View, @class.OrgUnitId, ct);
        if (check.Problem is not null)
        {
            return check.Problem;
        }

        if (termId is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "termId is required.");
        }

        if (await timetable.FindTermAsync(@class, termId.Value, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        // Only the slots of this class and term; drafts only for staff who can manage (the same rule as the grid).
        var manage = (await access.EvaluateAsync(Permissions.Timetable.Manage, @class.OrgUnitPath, ct)).Allowed;
        var slots = await timetable.SlotsAsync(id, termId.Value, manage, ct);
        var dtos = await timetable.ToDtosAsync(@class.OrganizationId, slots, null, ct);
        var unit = await db.OrgUnits.SingleAsync(u => u.Id == @class.OrgUnitId, ct);
        var (periodRows, _) = await periods.EffectiveAsync(unit, ct);
        var workingDays = (await settings.ResolveAsync(SettingKeys.TimetableWorkingDays, @class.OrgUnitPath, ct)).Value;
        var days = Enum.GetValues<DayOfWeek>().Where(d => workingDays.Contains(d.ToString()[..3])).ToList();

        var stream = new MemoryStream();
        TimetableExporter.Write(
            @class.NameEn.Length > 0 ? @class.NameEn : @class.NameAr,
            periodRows.OrderBy(p => p.Order).Select(p => new TimetableExporter.PeriodRow(p.NameAr, p.NameEn, p.StartTime, p.EndTime, p.Id)).ToList(),
            days, dtos, stream);
        stream.Position = 0;
        await audit.WriteSecurityEventAsync("TimetableExported", context.UserId, $"class={id};term={termId};rows={dtos.Count}", cancellationToken: ct);
        return Results.File(stream, XlsxContentType, $"timetable-{id:N}.xlsx");
    }
}
