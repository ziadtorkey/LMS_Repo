using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Guardians;

/// <summary>The guardians of one student: list, link, change relation or primary, unlink.</summary>
public static class StudentGuardianEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(GuardianRoutes.StudentGuardiansPattern, ListAsync).RequirePermission(Permissions.Students.View);
        app.MapPost(GuardianRoutes.StudentGuardiansPattern, LinkAsync).RequirePermission(Permissions.Students.Update);
        app.MapPut(GuardianRoutes.StudentGuardianPattern, UpdateAsync).RequirePermission(Permissions.Students.Update);
        app.MapDelete(GuardianRoutes.StudentGuardianPattern, UnlinkAsync).RequirePermission(Permissions.Students.Update);
    }

    /// <summary>
    /// The student inside the caller's scope (a student of another unit does not exist for the caller), then the permission at
    /// the student's home unit. The relationship rule of Students.View (Teacher: own classes only) joins here with M3 Task 12.
    /// </summary>
    private static async Task<(StudentProfile? Student, IResult? Problem)> FindAsync(
        Guid id, string permission, HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var student = context.OrganizationId is null ? null : await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, student.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (student, null);
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.View, http, db, context, ct);
        return problem ?? Results.Ok(await GuardianMapper.LinksOfAsync(db, student!, ct));
    }

    private static async Task<IResult> LinkAsync(
        Guid id, GuardianLinkInput? body, HttpContext http, LmsDbContext db, ICurrentContext context, IGuardianLinker linker, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, db, context, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (body is null)
        {
            return GuardianValidators.ToProblem(new GuardianLinkException(Lms.Shared.ProblemCodes.validation_failed, "body"));
        }

        try
        {
            var created = await linker.LinkAsync(student!, [GuardianValidators.ToDomain(body)], ct);
            var dto = (await GuardianMapper.LinksOfAsync(db, student!, ct)).Single(l => l.GuardianId == created[0].GuardianId);
            return Results.Created(GuardianRoutes.StudentGuardian(id, dto.GuardianId), dto);
        }
        catch (GuardianLinkException ex)
        {
            return GuardianValidators.ToProblem(ex);
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid guardianId, UpdateStudentGuardianRequest? body, HttpContext http, LmsDbContext db, ICurrentContext context,
        IGuardianLinker linker, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, db, context, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            await linker.UpdateLinkAsync(student!, guardianId, body?.Relation ?? string.Empty, body?.IsPrimary ?? false, ct);
            return Results.Ok((await GuardianMapper.LinksOfAsync(db, student!, ct)).Single(l => l.GuardianId == guardianId));
        }
        catch (GuardianLinkException ex)
        {
            return GuardianValidators.ToProblem(ex);
        }
    }

    private static async Task<IResult> UnlinkAsync(
        Guid id, Guid guardianId, HttpContext http, LmsDbContext db, ICurrentContext context, IGuardianLinker linker, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, db, context, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            await linker.UnlinkAsync(student!, guardianId, ct);
            return Results.NoContent();
        }
        catch (GuardianLinkException ex)
        {
            return GuardianValidators.ToProblem(ex);
        }
    }
}
