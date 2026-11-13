using System.Text.Json;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The change request queue (M3 Task 22, #230): the request belongs to a student of School1. It needs Students.ApproveChanges
/// (admins, not a Teacher) at the student's unit; School2 and other clients get 404. The student's own /api/me routes are
/// self-scoped and covered by SelfEditTests / ChangeRequestTests.
/// </summary>
public sealed class StudentChangeRequestsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("change requests list", HttpMethod.Get, Routes.StudentChangeRequests,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.StudentChangeRequests)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, NotFound)),
        new("change requests get", HttpMethod.Get, Routes.StudentChangeRequestPattern,
            async (w, _) => new ProbeRequest(Routes.StudentChangeRequest(await NewRequestAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("change requests approve", HttpMethod.Post, Routes.StudentChangeRequestApprovePattern,
            async (w, _) => new ProbeRequest(Routes.StudentChangeRequestApprove(await NewRequestAsync(w)), new DecideChangeRequestRequest("ok")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        new("change requests reject", HttpMethod.Post, Routes.StudentChangeRequestRejectPattern,
            async (w, _) => new ProbeRequest(Routes.StudentChangeRequestReject(await NewRequestAsync(w)), new DecideChangeRequestRequest("no")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
    ];

    /// <summary>A pending request of a fresh School1 student of client A.</summary>
    private static async Task<Guid> NewRequestAsync(M1ProbeWorld w)
    {
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var student = new StudentProfile(
            w.Callers.A.School1, "Q" + Guid.NewGuid().ToString("N")[..10], "طالب", "Probe student " + w.Next(),
            new DateOnly(2014, 5, 1), "Male", "Muslim", "EG");
        db.Set<StudentProfile>().Add(student);
        await db.SaveChangesAsync();
        var changes = JsonSerializer.Serialize(new[] { new FieldChangeDto(StudentFields.NameEn, student.NameEn, "Probe renamed " + w.Next()) });
        var request = new StudentChangeRequest(student, Guid.NewGuid(), changes, null);
        db.Set<StudentChangeRequest>().Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }
}
