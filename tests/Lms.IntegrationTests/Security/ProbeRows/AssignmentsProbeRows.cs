using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Role assignments (Task 21, #107): checked at the target unit (below School1); sibling/other client 404, no
/// Assignments permission 403. Targets are created as the root admin.
/// </summary>
public sealed class AssignmentsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("assignments list for unit", HttpMethod.Get, Routes.OrgUnitAssignmentsPattern,
            async (w, _) => new ProbeRequest(Routes.OrgUnitAssignments((await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("assignments create", HttpMethod.Post, Routes.Assignments,
            async (w, _) => new ProbeRequest(Routes.Assignments, await AssignmentBodyAsync(w, (await w.NewUnitAsync(w.Callers.A.School1)).Id)),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("assignments update", HttpMethod.Put, Routes.AssignmentPattern,
            async (w, _) => new ProbeRequest(Routes.Assignment(await NewAssignmentAsync(w)), new UpdateAssignmentRequest(null, null)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("assignments delete", HttpMethod.Delete, Routes.AssignmentPattern,
            async (w, _) => new ProbeRequest(Routes.Assignment(await NewAssignmentAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static async Task<CreateAssignmentRequest> AssignmentBodyAsync(M1ProbeWorld w, Guid unitId)
    {
        // A real user each time, so the probe never sends an invitation email.
        var email = $"probe-{w.Next()}@lms.test";
        await using (var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Users.Add(new User { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayNameAr = "مستخدم", DisplayNameEn = "Probe user" });
            await db.SaveChangesAsync();
        }

        return new CreateAssignmentRequest(email, null, null, w.Callers.A.Roles[BuiltInRoles.Teacher].Id, unitId, null, null);
    }

    private static async Task<Guid> NewAssignmentAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Assignments,
            await AssignmentBodyAsync(w, (await w.NewUnitAsync(w.Callers.A.School1)).Id))).GetProperty("id").GetGuid();
}
