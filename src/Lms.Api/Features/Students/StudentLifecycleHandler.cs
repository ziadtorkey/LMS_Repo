using System.Text.Json;
using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students;

/// <summary>
/// The writes after creation: edit (with a home move), status, delete, per-student feature flags and password reset. Every one
/// starts with the student inside the caller's scope and relationship filter (404 otherwise), then the permission at their unit.
/// </summary>
public sealed class StudentLifecycleHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, ISettingsResolver settings, IStudentCodeGenerator codes,
    IPersonDuplicateFinder duplicates, IPersonStatusService statuses, IRoleAssignmentSync roles, IEnrollmentEngine enrollment, IStudentCredentials credentials,
    UserManager<User> users, StudentQueries queries, StudentMapper mapper)
{
    private const int MaxReason = 500;

    private async Task<(StudentProfile? Student, IResult? Problem)> FindAsync(
        Guid id, string permission, HttpContext http, CancellationToken ct)
    {
        var student = await queries.FindVisibleAsync(id, ct);
        if (student is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, student.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (student, null);
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateStudentRequest? request, HttpContext http, CancellationToken ct)
    {
        if (request is null)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["body"] = ["required"] });
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (StudentValidators.Validate(request.Data, today, out var data) is { } invalid)
        {
            return invalid;
        }

        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        Lms.Domain.Organizations.OrgUnit? newHome = null;
        if (request.HomeOrgUnitId != student!.OrgUnitId)
        {
            var target = await http.CheckUnitAsync(Permissions.Students.Update, request.HomeOrgUnitId, ct);
            if (target.Problem is not null)
            {
                return target.Problem;
            }

            newHome = target.Unit!;
            var level = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == newHome.LevelTypeId, ct);
            if (level is not { CanHostClasses: true })
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
            }

            // A placed student moves between schools by a transfer (it also moves the placement), never by editing the profile.
            if (await db.Set<ClassStudent>().AnyAsync(c => c.StudentId == id && c.Status == ClassStudent.ActiveStatus, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.already_placed);
            }
        }

        var mode = (await settings.ResolveAsync(SettingKeys.StudentsCodeMode, student.OrgUnitPath, ct)).Value;
        var requestedCode = string.IsNullOrWhiteSpace(request.StudentCode) ? student.StudentCode : request.StudentCode.Trim();
        var codeChanged = !string.Equals(requestedCode, student.StudentCode, StringComparison.Ordinal);
        if (codeChanged)
        {
            if (mode != StudentCodeModes.Manual)
            {
                return ApiProblems.BadRequest(ProblemCodes.student_code_not_allowed);
            }

            if (await codes.ValidateManualAsync(student.OrganizationId, requestedCode, id, ct) is { } codeProblem)
            {
                return codeProblem == ProblemCodes.student_code_taken ? ApiProblems.Conflict(codeProblem) : ApiProblems.BadRequest(codeProblem);
            }
        }

        if (data.NationalId is not null && await duplicates.StudentNationalIdExistsAsync(student.OrganizationId, data.NationalId, id, ct))
        {
            return StudentProblems.Duplicate("nationalId");
        }

        if (!request.ConfirmPossibleDuplicate)
        {
            var same = await duplicates.CountStudentsByNameAndDobAsync(
                student.OrganizationId, PersonNormalizer.NameKey(data.NameAr), PersonNormalizer.NameKey(data.NameEn), data.DateOfBirth, id, ct);
            if (same > 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.possible_duplicate, ["count"] = same });
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        student.Update(
            data.NameAr, data.NameEn, data.NicknameAr, data.NicknameEn, data.DateOfBirth, data.Gender, data.Religion, data.Nationality,
            data.NationalId, data.Mobile, data.Email, data.PhotoFileId);
        if (codeChanged)
        {
            student.ChangeCode(requestedCode);
        }

        if (newHome is not null)
        {
            student.MoveHome(newHome);
            foreach (var link in await db.Set<StudentGuardian>().Where(l => l.StudentId == id).ToListAsync(ct))
            {
                link.Repoint(student); // guardian links follow the student
            }

            foreach (var change in await db.Set<PersonStatusChange>().Where(c => c.Kind == PersonKind.Student && c.ProfileId == id).ToListAsync(ct))
            {
                change.MoveHome(newHome.Id, newHome.Path); // the history follows the student, as for staff
            }
        }

        await db.SaveChangesAsync(ct);
        if (student.UserId is { } userId)
        {
            if (codeChanged && await users.FindByIdAsync(userId.ToString()) is { } account
                && string.Equals(account.UserName, student.StudentCode, StringComparison.OrdinalIgnoreCase) is false
                && account.UserName is { } oldName && !oldName.Contains('@'))
            {
                await users.SetUserNameAsync(account, requestedCode.ToLowerInvariant()); // the user name is the student code
            }

            if (newHome is not null)
            {
                await roles.SyncBuiltInAsync(userId, student.OrganizationId, BuiltInRoles.Student, [newHome], ct);
            }
        }

        await transaction.CommitAsync(ct);
        return Results.Ok(await mapper.DetailAsync(student, ct));
    }

    public async Task<IResult> SetStatusAsync(Guid id, SetPersonStatusRequest? request, HttpContext http, CancellationToken ct)
    {
        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (request is null || (request.Status != PersonStatuses.Active && request.Status != PersonStatuses.Deactivated))
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Status"] = ["invalid_value"] });
        }

        if (reason.Length is 0 or > MaxReason)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Reason"] = [reason.Length == 0 ? "required" : "too_long"] });
        }

        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var to = Enum.Parse<PersonStatus>(request.Status);
        if (student!.Status != to)
        {
            await statuses.ChangeAsync(PersonKind.Student, student.Id, to, reason, ct);
        }

        return Results.Ok(await mapper.DetailAsync(student, ct));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.Delete, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var placement in await db.Set<ClassStudent>().Where(c => c.StudentId == id && c.Status == ClassStudent.ActiveStatus).ToListAsync(ct))
        {
            await enrollment.RemoveFromClassAsync(placement, "Student deleted", DateOnly.FromDateTime(clock.UtcNow), ct);
        }

        foreach (var link in await db.Set<StudentGuardian>().Where(l => l.StudentId == id).ToListAsync(ct))
        {
            db.Remove(link); // soft delete; guardians stay (siblings may share them)
        }

        // Soft delete by marking: Remove() would try to cascade over the tracked placements of this request.
        student!.MarkDeleted(clock.UtcNow, context.UserId);
        await db.SaveChangesAsync(ct);
        if (student!.UserId is { } userId)
        {
            await roles.SyncBuiltInAsync(userId, student.OrganizationId, BuiltInRoles.Student, [], ct); // the Student assignment goes
            await statuses.RecomputeUserStatusAsync(userId, ct);
        }

        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> SetFeatureFlagsAsync(Guid id, SetStudentFeatureFlagsRequest? request, HttpContext http, CancellationToken ct)
    {
        var known = FeatureFlags.All.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        if (request?.Overrides is null || request.Overrides.Keys.Any(k => !known.Contains(k)))
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Overrides"] = ["invalid_value"] });
        }

        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        // null = inherit: the key is removed so the setting of the unit decides again
        var current = Lms.Infrastructure.People.Students.StudentFeatureResolver.Parse(student!.FeatureFlagOverridesJson).ToDictionary(p => p.Key, p => p.Value);
        foreach (var (name, value) in request.Overrides)
        {
            if (value is { } on)
            {
                current[name] = on;
            }
            else
            {
                current.Remove(name);
            }
        }

        student.SetFeatureFlagOverrides(JsonSerializer.Serialize(current));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await mapper.DetailAsync(student, ct));
    }

    public async Task<IResult> ResetPasswordAsync(Guid id, ResetStudentPasswordRequest? request, HttpContext http, CancellationToken ct)
    {
        var (student, problem) = await FindAsync(id, Permissions.Students.Update, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var issued = await credentials.IssueAsync(student!, request?.DefaultPassword, reset: true, ct);
            await db.SaveChangesAsync(ct);
            if (issued.User.Id != student!.UserId)
            {
                await roles.SyncBuiltInAsync(issued.User.Id, student.OrganizationId, BuiltInRoles.Student, [await HomeAsync(student, ct)], ct);
            }

            await transaction.CommitAsync(ct);
            await credentials.FlushDeliveriesAsync(ct);
            return Results.Ok(new StudentCreatedDto(await mapper.DetailAsync(student, ct), issued.Password ?? string.Empty, issued.DeliveredTo, issued.User.UserName ?? string.Empty));
        }
        catch (Exception ex) when (StudentProblems.From(ex) is { } refused)
        {
            return refused;
        }
    }

    private async Task<Lms.Domain.Organizations.OrgUnit> HomeAsync(StudentProfile student, CancellationToken ct) =>
        await db.Set<Lms.Domain.Organizations.OrgUnit>().FirstAsync(u => u.Id == student.OrgUnitId, ct);
}
