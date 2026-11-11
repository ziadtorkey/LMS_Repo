using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// What each M2 staff caller (<see cref="M2Caller"/>) must get from every M2 route, keyed by method and route pattern.
/// The rows themselves live with their feature PRs (<c>Security/ProbeRows/&lt;Module&gt;ProbeRows.cs</c>) and answer for the
/// seven M1 callers; <see cref="M2EndpointMatrixTests"/> runs the same requests as the manager, counselor, supervisor and a
/// client-B teacher and compares with this table. A route missing here is listed (not failed) and checked at PR review.
///
/// Permissions behind the answers (M2 Task 1, D32): the Manager holds Staff.View and Staff.Create, Assignments.Create and
/// Assignments.Delete; the Counselor and Supervisor hold Staff.View only; a Teacher of another client holds nothing, and a
/// client-B caller never sees client A anyway. Order: manager, counselor, supervisor, client-B teacher.
/// </summary>
internal static class M2StaffCallerExpectations
{
    private static readonly IReadOnlyDictionary<string, Dictionary<M2Caller, int>> Table = new Dictionary<string, Dictionary<M2Caller, int>>
    {
        // Staff (Tasks 10, 11, 16): read with Staff.View, create with Staff.Create at the home unit.
        ["GET /api/staff"] = M2(Ok, Ok, Ok, Forbidden),
        ["POST /api/staff"] = M2(Created, Forbidden, Forbidden, Forbidden),
        ["GET /api/staff/{id:guid}"] = M2(Ok, Ok, Ok, Forbidden),
        // D32: edit, status, delete and resend need Staff.Update / Staff.Delete, which a Manager lacks.
        ["PUT /api/staff/{id:guid}"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["PUT /api/staff/{id:guid}/status"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["DELETE /api/staff/{id:guid}"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["POST /api/staff/{id:guid}/resend-invitation"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        // Share: Assignments.Create passes for a Manager, then Staff.Update at the home unit (the owner) is refused.
        ["POST /api/staff/{id:guid}/shares"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        // Unshare needs only Assignments.Delete at the share's unit (D16): a Manager at School1 may remove a share there.
        // The probe's share lies outside School1 (BE row "staff unshare (share at School2)"), so the Manager is refused with 404.
        ["DELETE /api/staff/{id:guid}/shares/{assignmentId:guid}"] = M2(NotFound, Forbidden, Forbidden, Forbidden),

        // Staff import (Tasks 7, 17): Staff.Import, held by the Org Admin only.
        ["GET /api/imports/{kind}/columns"] = NoImport(),
        ["GET /api/imports/{kind}/template"] = NoImport(),
        ["POST /api/imports/{kind}"] = NoImport(),
        ["GET /api/imports/{kind}"] = NoImport(),
        ["GET /api/imports/{kind}/{id:guid}"] = NoImport(),
        ["GET /api/imports/{kind}/{id:guid}/rows"] = NoImport(),
        ["GET /api/imports/{kind}/{id:guid}/error-report"] = NoImport(),
        ["POST /api/imports/{kind}/{id:guid}/commit"] = NoImport(),
        ["DELETE /api/imports/{kind}/{id:guid}"] = NoImport(),

        // Identity providers (Task 21): client root and IdentityProviders.*, Org Admin only.
        ["GET /api/identity-providers"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["POST /api/identity-providers"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["PUT /api/identity-providers/{id:guid}"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["DELETE /api/identity-providers/{id:guid}"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        ["POST /api/identity-providers/{id:guid}/test"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),

        // Development-only job trigger (Task 4/19): Clients.Update, a platform admin only.
        ["POST /api/dev/jobs/{jobId}/run"] = M2(Forbidden, Forbidden, Forbidden, Forbidden),
    };

    public static string Key(HttpMethod method, string pattern) => $"{method.Method.ToUpperInvariant()} {pattern}";

    public static bool TryGet(HttpMethod method, string pattern, out IReadOnlyDictionary<M2Caller, int> expected)
    {
        var found = Table.TryGetValue(Key(method, pattern), out var row);
        expected = row ?? new Dictionary<M2Caller, int>();
        return found;
    }

    public static IEnumerable<string> Keys => Table.Keys;

    private static Dictionary<M2Caller, int> NoImport() => M2(Forbidden, Forbidden, Forbidden, Forbidden);
}
