using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>/api/me/student-profile/change-requests: a student files, lists and withdraws requests for critical fields (FR-STU-04).</summary>
internal sealed class MyChangeRequestsHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, MyStudentProfileHandler me, StudentChangeApplier applier,
    ChangeRequestMapper mapper, ChangeRequestNotifier notifier)
{
    private const int MaxNote = 1000;

    public async Task<IResult> CreateAsync(CreateChangeRequestRequest? request, CancellationToken ct)
    {
        if (request?.Changes is not { Count: > 0 } changes)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Changes"] = ["required"] });
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > MaxNote })
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Note"] = ["too_long"] });
        }

        var student = await me.CurrentAsync(ct);
        if (student is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        var (_, requestable) = StudentFieldRules.Classify(await applier.CriticalAsync(student, ct));
        if (changes.Keys.Any(f => !requestable.Contains(f)))
        {
            return MyStudentProfileHandler.CriticalField(changes.Keys.Where(f => !requestable.Contains(f)));
        }

        if (await db.Set<StudentChangeRequest>().AnyAsync(r => r.StudentId == student.Id && r.Status == ChangeRequestStatus.Pending, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.change_request_pending);
        }

        var (data, problem) = await applier.PrepareAsync(student, changes, ct);
        if (problem is not null)
        {
            return problem;
        }

        var list = changes.Keys
            .Select(f => new FieldChangeDto(f, StudentFieldValues.Read(student, f), StudentFieldValues.ReadClean(data!, f)))
            .Where(c => !StudentFieldValues.Same(c.Before, c.After))
            .ToList();
        if (list.Count == 0)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Changes"] = ["no_change"] });
        }

        var entity = new StudentChangeRequest(student, userId, JsonSerializer.Serialize(list), note);
        db.Set<StudentChangeRequest>().Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // the unique index on PendingStudentId: another request of this student won the race
            return ApiProblems.Conflict(ProblemCodes.change_request_pending);
        }

        await notifier.RequestedAsync(student, ct);
        return Results.Created(Routes.MyChangeRequest(entity.Id), (await mapper.ToDtosAsync([entity], ct))[0]);
    }

    public async Task<IResult> ListAsync(CancellationToken ct)
    {
        var student = await me.CurrentAsync(ct);
        if (student is null)
        {
            return ApiProblems.NotFound();
        }

        var rows = await db.Set<StudentChangeRequest>().Where(r => r.StudentId == student.Id)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(ct);
        return Results.Ok(await mapper.ToDtosAsync(rows, ct));
    }

    public async Task<IResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var student = await me.CurrentAsync(ct);
        var request = student is null ? null : await db.Set<StudentChangeRequest>().FirstOrDefaultAsync(r => r.Id == id && r.StudentId == student.Id, ct);
        if (request is null)
        {
            return ApiProblems.NotFound();
        }

        if (!request.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.change_request_closed);
        }

        request.Cancel(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
