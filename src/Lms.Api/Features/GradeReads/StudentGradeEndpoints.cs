using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.Grading.Reads;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.GradeReads;

/// <summary>
/// The grades of a student as the student and the guardians see them (M5 L6): through <see cref="PublishedGradeReads"/> only, which loads
/// published items only. <c>/api/me/grades</c> is for a signed-in student (a guardian or staff member is 403); a guardian reads a child
/// through <c>/api/students/{id}/grades</c> and a child that is not theirs is 404, like a child that does not exist.
/// </summary>
public static class StudentGradeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MeGrades, MineAsync).RequirePermission(Permissions.Grades.ViewOwn);
        app.MapGet(Routes.StudentGradesPattern, ChildAsync).RequirePermission(Permissions.Grades.ViewOwn);
    }

    private static async Task<IResult> MineAsync(
        Guid? termId, LmsDbContext db, ICurrentContext context, PublishedGradeReads reads, CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return Forbidden();
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return student is null ? Forbidden() : Results.Ok(await reads.ForStudentAsync(student, termId, ct));
    }

    private static async Task<IResult> ChildAsync(
        Guid id, Guid? termId, LmsDbContext db, ICurrentContext context, PublishedGradeReads reads, CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        var guardianId = await db.Set<GuardianProfile>().Where(g => g.UserId == userId).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        if (guardianId is null || !await db.Set<StudentGuardian>().AnyAsync(l => l.GuardianId == guardianId && l.StudentId == id, ct))
        {
            return ApiProblems.NotFound();
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == id, ct);
        return student is null ? ApiProblems.NotFound() : Results.Ok(await reads.ForStudentAsync(student, termId, ct));
    }

    private static IResult Forbidden() => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
}
