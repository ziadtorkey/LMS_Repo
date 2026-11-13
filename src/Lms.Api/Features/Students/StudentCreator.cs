using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;

namespace Lms.Api.Features.Students;

/// <summary>What one new student is made of. <c>Code</c> null = generated (Auto mode); validation has already happened.</summary>
internal sealed record StudentDraft(
    OrgUnit Home, CleanStudentData Data, string? Code, IReadOnlyList<GuardianLink> Guardians, string? DefaultPassword,
    Class? Placement, bool ConfirmOverCapacity, DateOnly Today);

internal sealed record CreatedStudent(StudentProfile Student, IssuedCredentials Issued);

/// <summary>
/// The writes of creating one student (D24, D25, D45), shared by POST /api/students and the Excel import: the profile with its
/// code, the guardians, the login account, the Student role at the home unit and the optional class placement. It joins the
/// caller's transaction and never commits; the credentials emails go out when the caller calls
/// <see cref="IStudentCredentials.FlushDeliveriesAsync"/> after its commit.
/// </summary>
internal sealed class StudentCreator(
    LmsDbContext db, IStudentCodeGenerator codes, IGuardianLinker guardians, IStudentCredentials credentials,
    IRoleAssignmentSync roles, IEnrollmentEngine enrollment)
{
    public async Task<CreatedStudent> CreateAsync(StudentDraft draft, CancellationToken ct)
    {
        var data = draft.Data;
        var code = draft.Code ?? await codes.GenerateAsync(draft.Home, ct);
        var student = new StudentProfile(draft.Home, code, data.NameAr, data.NameEn, data.DateOfBirth, data.Gender, data.Religion, data.Nationality);
        student.Update(
            data.NameAr, data.NameEn, data.NicknameAr, data.NicknameEn, data.DateOfBirth, data.Gender, data.Religion, data.Nationality,
            data.NationalId, data.Mobile, data.Email, data.PhotoFileId);
        db.Set<StudentProfile>().Add(student);
        await db.SaveChangesAsync(ct);

        if (draft.Guardians.Count > 0)
        {
            await guardians.LinkAsync(student, draft.Guardians, ct);
        }

        await guardians.EnsureRequirementAsync(student, draft.Guardians.Count, ct);

        var issued = await credentials.IssueAsync(student, draft.DefaultPassword, reset: false, ct);
        await db.SaveChangesAsync(ct);
        await roles.SyncBuiltInAsync(issued.User.Id, draft.Home.OrganizationId, BuiltInRoles.Student, [draft.Home], ct);

        if (draft.Placement is not null)
        {
            await enrollment.PlaceAsync(student, draft.Placement, draft.Today, draft.ConfirmOverCapacity, ct);
        }

        return new CreatedStudent(student, issued);
    }
}
