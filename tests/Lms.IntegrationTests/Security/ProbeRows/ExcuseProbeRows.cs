using Lms.Domain.Excuses;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using static Lms.IntegrationTests.Access.AccessTestData;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The review side of absence excuses (M5 Task 19): the counselor queue, reject, escalate and the escalation targets. Every excuse belongs to a student
/// of School1. School 2 and the other client reach neither the excuse (404, outside their subtree), a Teacher lacks the permission (403), the root
/// and School 1 admins and the platform admin pass. The escalate target is a Manager assigned for the row, so no caller is the target.
/// </summary>
public sealed class ExcuseProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M5 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("excuse review queue", HttpMethod.Get, Routes.ExcuseReviewPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ExcuseReviewPattern)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("excuse reject", HttpMethod.Post, Routes.ExcuseRejectPattern,
            async (w, _) => new ProbeRequest(Routes.ExcuseReject(await NewExcuseAsync(w)), new RejectExcuseRequest("not enough evidence")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("excuse escalate", HttpMethod.Post, Routes.ExcuseEscalatePattern,
            async (w, _) =>
            {
                var target = await NewManagerAsync(w);
                return new ProbeRequest(Routes.ExcuseEscalate(await NewExcuseAsync(w)), new EscalateExcuseRequest(target, "needs a manager"));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("excuse approve", HttpMethod.Post, Routes.ExcuseApprovePattern,
            async (w, _) => new ProbeRequest(Routes.ExcuseApprove(await NewExcuseAsync(w)), new ApproveExcuseRequest("ok")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        // The submit side is for a guardian or the student: staff hold no Excuses.Submit (403), the platform admin has no link to any student (404).
        new("excuse policy of a student", HttpMethod.Get, Routes.StudentExcusePolicyPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.StudentExcusePolicy(Guid.NewGuid()))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("excusable sessions of a student", HttpMethod.Get, Routes.StudentExcusableSessionsPattern,
            (_, _) => Task.FromResult(new ProbeRequest($"{Routes.StudentExcusableSessionsPattern.Replace("{id:guid}", Guid.NewGuid().ToString())}?from=2027-02-01&to=2027-02-05")),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("excuse submit", HttpMethod.Post, Routes.ExcusesPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ExcusesPattern,
                new SubmitExcuseRequest(Guid.NewGuid(), "Illness", null, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 2), null, null))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("my excuses", HttpMethod.Get, Routes.MeExcuses,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MeExcuses)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok)),
        new("excuse withdraw", HttpMethod.Post, Routes.ExcuseWithdrawPattern,
            async (w, _) => new ProbeRequest(Routes.ExcuseWithdraw(await NewExcuseAsync(w))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("excuse escalation targets", HttpMethod.Get, Routes.ExcuseEscalationTargetsPattern,
            async (w, _) => new ProbeRequest(Routes.ExcuseEscalationTargets(await NewExcuseAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static int _counter;

    /// <summary>A pending excuse for a student of School1, submitted by an unrelated user (a guardian stand-in).</summary>
    private static async Task<Guid> NewExcuseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Callers.A.School1, "XP" + Interlocked.Increment(ref _counter));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var excuse = new AbsenceExcuse(student, Guid.NewGuid(), "Family", null, new DateOnly(2027, 2, 4), new DateOnly(2027, 2, 4), null, DateTime.UtcNow);
        db.Add(excuse);
        await db.SaveChangesAsync();
        return excuse.Id;
    }

    /// <summary>A Manager at the client root: an eligible escalation target that is none of the probing callers.</summary>
    private static async Task<Guid> NewManagerAsync(M1ProbeWorld w)
    {
        var user = await PeopleTestKit.AddUserAsync(w.Callers.Factory);
        await AssignAsync(w.Callers.Factory, w.Callers.A, w.Callers.A.Root, BuiltInRoles.Manager, user.Id);
        return user.Id;
    }
}
