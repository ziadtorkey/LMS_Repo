using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students;

/// <summary>
/// POST /api/students: the profile with its code, the guardians, the login account with its one-time default password, the
/// Student role at the home unit and, optionally, the class placement, in one transaction; the credentials email goes out only
/// after the commit (D24, D25, D45).
/// </summary>
public sealed class CreateStudentHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, ISettingsResolver settings, IStudentCodeGenerator codes,
    IPersonDuplicateFinder duplicates, IGuardianLinker guardians, IStudentCredentials credentials, IRoleAssignmentSync roles,
    IEnrollmentEngine enrollment, StudentMapper mapper)
{
    public async Task<IResult> HandleAsync(CreateStudentRequest? request, HttpContext http, CancellationToken ct)
    {
        if (request is null)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["body"] = ["required"] });
        }

        // A platform admin without a client context acts on no client's students.
        if (context.OrganizationId is null)
        {
            return ApiProblems.NotFound();
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (StudentValidators.Validate(request.Data, today, out var data) is { } invalid)
        {
            return invalid;
        }

        var access = await http.CheckUnitAsync(Permissions.Students.Create, request.HomeOrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var home = access.Unit!;
        var level = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == home.LevelTypeId, ct);
        if (level is not { CanHostClasses: true })
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
        }

        Class? cls = null;
        if (request.ClassId is { } classId)
        {
            cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == classId, ct);
            if (cls is null)
            {
                return ApiProblems.NotFound();
            }

            if (cls.OrgUnitId != home.Id)
            {
                return ApiProblems.BadRequest(ProblemCodes.class_unit_mismatch);
            }
        }

        var guardianInputs = request.Guardians ?? [];
        if (guardianInputs.Any(g => g is null))
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Guardians"] = ["invalid"] });
        }

        // Code mode is a setting of the home unit (D25): Auto generates, Manual is typed and must be free.
        var mode = (await settings.ResolveAsync(SettingKeys.StudentsCodeMode, home.Path, ct)).Value;
        var requestedCode = string.IsNullOrWhiteSpace(request.StudentCode) ? null : request.StudentCode.Trim();
        if (mode == StudentCodeModes.Auto && requestedCode is not null)
        {
            return ApiProblems.BadRequest(ProblemCodes.student_code_not_allowed);
        }

        if (mode == StudentCodeModes.Manual)
        {
            if (requestedCode is null)
            {
                return ApiProblems.BadRequest(ProblemCodes.student_code_required);
            }

            if (await codes.ValidateManualAsync(home.OrganizationId, requestedCode, null, ct) is { } codeProblem)
            {
                return codeProblem == ProblemCodes.student_code_taken ? ApiProblems.Conflict(codeProblem) : ApiProblems.BadRequest(codeProblem);
            }
        }

        if (data.NationalId is not null && await duplicates.StudentNationalIdExistsAsync(home.OrganizationId, data.NationalId, null, ct))
        {
            return StudentProblems.Duplicate("nationalId");
        }

        if (!request.ConfirmPossibleDuplicate)
        {
            var same = await duplicates.CountStudentsByNameAndDobAsync(
                home.OrganizationId, PersonNormalizer.NameKey(data.NameAr), PersonNormalizer.NameKey(data.NameEn), data.DateOfBirth, null, ct);
            if (same > 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.possible_duplicate, ["count"] = same });
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var code = requestedCode ?? await codes.GenerateAsync(home, ct);
            var student = new StudentProfile(home, code, data.NameAr, data.NameEn, data.DateOfBirth, data.Gender, data.Religion, data.Nationality);
            student.Update(
                data.NameAr, data.NameEn, data.NicknameAr, data.NicknameEn, data.DateOfBirth, data.Gender, data.Religion, data.Nationality,
                data.NationalId, data.Mobile, data.Email, data.PhotoFileId);
            db.Set<StudentProfile>().Add(student);
            await db.SaveChangesAsync(ct);

            if (guardianInputs.Count > 0)
            {
                await guardians.LinkAsync(student, guardianInputs.Select(Lms.Api.Features.Guardians.GuardianValidators.ToDomain).ToList(), ct);
            }

            await guardians.EnsureRequirementAsync(student, guardianInputs.Count, ct);

            var issued = await credentials.IssueAsync(student, request.DefaultPassword, reset: false, ct);
            await db.SaveChangesAsync(ct);
            await roles.SyncBuiltInAsync(issued.User.Id, home.OrganizationId, BuiltInRoles.Student, [home], ct);

            if (cls is not null)
            {
                await enrollment.PlaceAsync(student, cls, today, request.ConfirmOverCapacity, ct);
            }

            await transaction.CommitAsync(ct);
            await credentials.FlushDeliveriesAsync(ct);

            var detail = await mapper.DetailAsync(student, ct);
            return Results.Created(Routes.Student(student.Id), new StudentCreatedDto(detail, issued.Password ?? string.Empty, issued.DeliveredTo, issued.User.UserName ?? string.Empty));
        }
        catch (Exception ex) when (StudentProblems.From(ex) is { } problem)
        {
            return problem; // the transaction rolls back when it is disposed
        }
    }
}
