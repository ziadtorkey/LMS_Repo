using Lms.Domain.Organizations;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Staff (M2 Tasks 10, 11, 16): unit-targeted at the staff member's home unit. A sibling school or another client sees
/// nothing (404); a missing permission is 403 before any lookup. D32: a Manager keeps Staff View + Create only, so
/// edit, status, delete, resend and share are 403 for it. Removing a share needs Assignments.Delete at the share's unit,
/// which a Manager has (D16). Targets: a fresh staff member whose home is a fresh unit below School1, created by the root
/// admin through the API.
/// </summary>
public sealed class StaffProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("staff list (School1)", HttpMethod.Get, Routes.Staff,
            (w, _) => Task.FromResult(new ProbeRequest($"{Routes.Staff}?orgUnitId={w.Callers.A.School1.Id}")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok))
        {
            M2Expected = M2(Ok, Ok, Ok, Forbidden),
        },
        new("staff create (home below School1)", HttpMethod.Post, Routes.Staff,
            async (w, _) => new ProbeRequest(Routes.Staff, CreateBody(w, await w.NewUnitAsync(w.Callers.A.School1))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created))
        {
            M2Expected = M2(Created, Forbidden, Forbidden, Forbidden),
        },
        new("staff get", HttpMethod.Get, Routes.StaffPattern,
            async (w, _) => new ProbeRequest(Routes.StaffMember((await NewStaffAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok))
        {
            M2Expected = M2(Ok, Ok, Ok, Forbidden),
        },
        new("staff update", HttpMethod.Put, Routes.StaffPattern,
            async (w, _) =>
            {
                var staff = await NewStaffAsync(w);
                return new ProbeRequest(Routes.StaffMember(staff.Id), UpdateBody(w, staff.HomeId));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok))
        {
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
        new("staff status (deactivate)", HttpMethod.Put, Routes.StaffStatusPattern,
            async (w, _) => new ProbeRequest(
                Routes.StaffStatus((await NewStaffAsync(w)).Id), new SetPersonStatusRequest(PersonStatuses.Deactivated, "Probe")),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok))
        {
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
        new("staff delete", HttpMethod.Delete, Routes.StaffPattern,
            async (w, _) => new ProbeRequest(Routes.StaffMember((await NewStaffAsync(w)).Id)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent))
        {
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
        new("staff resend invitation (not activated)", HttpMethod.Post, Routes.StaffResendInvitationPattern,
            async (w, _) => new ProbeRequest(Routes.StaffResendInvitation((await NewStaffAsync(w)).Id)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent))
        {
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
        new("staff share (to a sibling unit below School1)", HttpMethod.Post, Routes.StaffSharesPattern,
            async (w, _) =>
            {
                var staff = await NewStaffAsync(w);
                var target = await w.NewUnitAsync(w.Callers.A.School1);
                return new ProbeRequest(Routes.StaffShares(staff.Id), new ShareStaffRequest([TeacherAt(w, target)]));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok))
        {
            // Manager has Assignments.Create but not Staff.Update at the home unit (the owner check).
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
        new("staff share remove", HttpMethod.Delete, Routes.StaffSharePattern,
            async (w, _) =>
            {
                var (staffId, assignmentId) = await NewSharedStaffAsync(w);
                return new ProbeRequest(Routes.StaffShare(staffId, assignmentId));
            },
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent))
        {
            // Assignments.Delete at the share's unit (inside School1): a Manager may remove it (D16).
            M2Expected = M2(NoContent, Forbidden, Forbidden, Forbidden),
        },
    ];

    internal sealed record StaffTarget(Guid Id, Guid HomeId);

    /// <summary>A fresh staff member (Teacher at its home) whose home is a fresh unit below School1.</summary>
    internal static async Task<StaffTarget> NewStaffAsync(M1ProbeWorld w)
    {
        var home = await w.NewUnitAsync(w.Callers.A.School1);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Staff, CreateBody(w, home));
        return new StaffTarget(created.GetProperty("summary").GetProperty("id").GetGuid(), home.Id);
    }

    private static async Task<(Guid StaffId, Guid AssignmentId)> NewSharedStaffAsync(M1ProbeWorld w)
    {
        var staff = await NewStaffAsync(w);
        var target = await w.NewUnitAsync(w.Callers.A.School1);
        var roles = await w.Callers.SendOkAsync(
            RootAdmin, HttpMethod.Post, Routes.StaffShares(staff.Id), new ShareStaffRequest([TeacherAt(w, target)]));
        var share = roles.EnumerateArray().Single(r => r.GetProperty("orgUnitId").GetGuid() == target.Id);
        return (staff.Id, share.GetProperty("assignmentId").GetGuid());
    }

    private static StaffRoleInput TeacherAt(M1ProbeWorld w, OrgUnit unit) =>
        new(w.Callers.A.Roles[BuiltInRoles.Teacher].Id, unit.Id, null, null);

    private static CreateStaffRequest CreateBody(M1ProbeWorld w, OrgUnit home)
    {
        var n = w.Next();
        return new CreateStaffRequest(
            "موظف " + n, "Probe staff " + n, $"probe-staff-{n}-{Guid.NewGuid():N}@probe.test", null, null, null, null, null,
            home.Id, null, [TeacherAt(w, home)]);
    }

    private static UpdateStaffRequest UpdateBody(M1ProbeWorld w, Guid homeId)
    {
        var n = w.Next();
        return new UpdateStaffRequest("موظف " + n, "Probe staff " + n, null, null, null, null, null, homeId, null);
    }
}
