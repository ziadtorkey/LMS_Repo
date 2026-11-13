using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>GET / PUT /api/me/student-profile: a student reads their own profile and edits the non-critical fields directly (D27).</summary>
internal sealed class MyStudentProfileHandler(
    LmsDbContext db, ICurrentContext context, StudentChangeApplier applier, StudentMapper mapper)
{
    /// <summary>The caller's own profile in the active client; null when the caller is not a student here.</summary>
    public async Task<StudentProfile?> CurrentAsync(CancellationToken ct) =>
        context.UserId is { } userId && context.OrganizationId is not null
            ? await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.UserId == userId, ct)
            : null;

    public async Task<IResult> GetAsync(CancellationToken ct)
    {
        var student = await CurrentAsync(ct);
        return student is null ? ApiProblems.NotFound() : Results.Ok(await ProfileAsync(student, ct));
    }

    public async Task<IResult> UpdateAsync(UpdateMyStudentProfileRequest? request, CancellationToken ct)
    {
        if (request?.Changes is not { Count: > 0 } changes)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Changes"] = ["required"] });
        }

        var student = await CurrentAsync(ct);
        if (student is null)
        {
            return ApiProblems.NotFound();
        }

        var (editable, _) = StudentFieldRules.Classify(await applier.CriticalAsync(student, ct));
        if (changes.Keys.Any(f => !editable.Contains(f)))
        {
            return CriticalField(changes.Keys.Where(f => !editable.Contains(f)));
        }

        var (data, problem) = await applier.PrepareAsync(student, changes, ct);
        if (problem is not null)
        {
            return problem;
        }

        StudentChangeApplier.Apply(student, data!);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ProfileAsync(student, ct));
    }

    public static IResult CriticalField(IEnumerable<string> fields) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad request",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.critical_field, ["fields"] = fields.ToArray() });

    private async Task<MyStudentProfileDto> ProfileAsync(StudentProfile student, CancellationToken ct)
    {
        var (editable, requestable) = StudentFieldRules.Classify(await applier.CriticalAsync(student, ct));
        return new MyStudentProfileDto(
            await mapper.DetailAsync(student, ct),
            StudentFields.Requestable.Where(editable.Contains).ToList(),
            StudentFields.Requestable.Where(requestable.Contains).ToList());
    }
}
