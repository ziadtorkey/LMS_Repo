using Lms.Api.Infrastructure;
using Lms.Domain.Attendance;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Attendance;

/// <summary>The counselor list of students who crossed the absence threshold (FR-EXC-04, D61): the unit subtree of the caller, active alerts by default.</summary>
public static class AttendanceAlertsEndpoint
{
    public static IEndpointRouteBuilder MapAttendanceAlerts(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AttendanceAlertsPattern, ListAsync).RequireAnyPermission(Permissions.Attendance.View, Permissions.Excuses.Review);
        return app;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] AttendanceAlertsQuery query, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var alerts = db.Set<AttendanceAlert>().AsQueryable(); // the table's filter limits the rows to the caller's subtree
        if (query.UnitId is { } unitId)
        {
            var access = await http.CheckUnitAsync(Permissions.Attendance.View, unitId, ct);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            var path = access.Unit!.Path;
            alerts = alerts.Where(a => a.OrgUnitPath.StartsWith(path));
        }

        if (query.ActiveOnly)
        {
            alerts = alerts.Where(a => a.ClearedAtUtc == null);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var total = await alerts.CountAsync(ct);
        var rows = await alerts.OrderByDescending(a => a.AlertedAtUtc).ThenBy(a => a.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var studentIds = rows.Select(r => r.StudentProfileId).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var items = rows.Select(a =>
        {
            var s = students.GetValueOrDefault(a.StudentProfileId);
            return new AttendanceAlertDto(a.Id, a.StudentProfileId, s?.NameAr ?? string.Empty, s?.NameEn ?? string.Empty, s?.StudentCode ?? string.Empty,
                a.OrgUnitId, a.Absences, a.Lates, a.WindowDays, a.Score, a.Threshold, a.AlertedAtUtc, a.ClearedAtUtc);
        }).ToList();
        return Results.Ok(new PagedResult<AttendanceAlertDto>(items, total, page, size));
    }
}
