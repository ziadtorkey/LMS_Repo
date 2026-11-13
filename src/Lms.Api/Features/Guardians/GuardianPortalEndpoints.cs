using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Guardians;

/// <summary>Guardian portal: (re)send the invitation (FR-STU-06, D30) and "my children" of the signed-in guardian.</summary>
public static class GuardianPortalEndpoints
{
    public const string PortalDisabledDetail = "GuardianPortalDisabled";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.GuardianPortalInvitationPattern, InviteAsync).RequireAuthorization().RequirePermission(Permissions.Students.Update);
        app.MapGet(Routes.MyChildren, MyChildrenAsync).RequireAuthorization().RequirePermission(Permissions.Students.ViewChildren);
    }

    private static async Task<IResult> InviteAsync(
        Guid id, LmsDbContext db, ICurrentContext context, IGuardianAccountSync sync, CancellationToken ct)
    {
        if (context.OrganizationId is null)
        {
            return ApiProblems.NotFound();
        }

        // A guardian is visible only through a child in the caller's scope.
        var guardian = await GuardianVisibility.Visible(db).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guardian is null)
        {
            return ApiProblems.NotFound();
        }

        var plan = await sync.PlanAsync(guardian, ct);
        if (!plan.HasEmail)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Email");
        }

        if (plan.EnabledUnits.Count == 0)
        {
            return ApiProblems.Conflict(ProblemCodes.validation_failed, PortalDisabledDetail);
        }

        if (plan.IsActivated)
        {
            return ApiProblems.Conflict(ProblemCodes.already_activated);
        }

        await sync.SyncAsync(guardian, resendInvitation: true, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// The children of the guardian profile of the signed-in user in the active client. The link table carries the unit scope,
    /// so the active context (a Guardian assignment at one child's unit) shows the children below that unit.
    /// </summary>
    private static async Task<IResult> MyChildrenAsync(LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        if (context.UserId is not { } userId || context.OrganizationId is null)
        {
            return Results.Ok(Array.Empty<MyChildDto>());
        }

        // The signed-in user's own guardian profile in this client (client filter of the table), nothing else of the table.
        var guardianId = await db.Set<GuardianProfile>().Where(g => g.UserId == userId).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        if (guardianId is null)
        {
            return Results.Ok(Array.Empty<MyChildDto>());
        }

        var rows = await (
                from link in db.Set<StudentGuardian>()
                join student in db.Set<StudentProfile>() on link.StudentId equals student.Id
                join unit in db.Set<OrgUnit>() on student.OrgUnitId equals unit.Id
                where link.GuardianId == guardianId
                orderby student.NameEn, student.Id
                select new { student, unit, link.Relation })
            .ToListAsync(ct);
        return Results.Ok(rows.Select(r => new MyChildDto(
            r.student.Id, r.student.StudentCode, r.student.NameAr, r.student.NameEn, r.unit.NameAr, r.unit.NameEn,
            r.student.PhotoFileId, r.Relation)).ToList());
    }
}
