using static Lms.IntegrationTests.Security.Caller;

namespace Lms.IntegrationTests.Security;

/// <summary>The concrete request one probe sends.</summary>
public sealed record ProbeRequest(string Url, object? Body = null);

/// <summary>
/// One row of the M1 probe matrix: an endpoint (method + route pattern exactly as mapped), how to build a request for a
/// caller (fresh targets are created here, as the root admin), and the expected status for every caller.
/// </summary>
public sealed record ProbeRow(
    string Name,
    HttpMethod Method,
    string Pattern,
    Func<M1ProbeWorld, Caller, Task<ProbeRequest>> Build,
    IReadOnlyDictionary<Caller, int> Expected)
{
    /// <summary>
    /// The milestone that owns the row. Rows of a closed milestone (1 and 2, the default) must be live: a route that is not
    /// mapped fails <see cref="M1EndpointMatrixTests"/>. Rows of the milestone in progress (3) are allowed to wait for their
    /// route and are only listed, until the milestone-end check is switched to strict.
    /// </summary>
    public int Milestone { get; init; } = 2;
}

/// <summary>
/// The probe rows of one feature module. One class per module under <c>Security/ProbeRows/</c> (named
/// <c>&lt;Module&gt;ProbeRows</c>), so PRs of different modules never edit the same file. Collected by
/// <see cref="M1ProbeRows"/> through reflection; a parameterless constructor is required.
/// </summary>
public interface IProbeRowModule
{
    IEnumerable<ProbeRow> Rows { get; }
}

/// <summary>Status codes and the per-caller expectation helper shared by every module file.</summary>
public static class ProbeExpect
{
    public const int Ok = 200, Created = 201, Accepted = 202, NoContent = 204, BadRequest = 400, Unauthorized = 401,
        Forbidden = 403, NotFound = 404, Conflict = 409;

    /// <summary>Expected status per caller, in the order of <see cref="Caller"/>.</summary>
    public static Dictionary<Caller, int> Expect(int root, int school1, int school2, int teacher, int clientB, int platform,
        int anonymous = Unauthorized) => new()
    {
        [RootAdmin] = root, [School1Admin] = school1, [School2Admin] = school2, [School1Teacher] = teacher,
        [ClientBAdmin] = clientB, [PlatformAdmin] = platform, [Anonymous] = anonymous,
    };

    /// <summary>Expected status per M2 caller, in the order of <see cref="M2Caller"/>.</summary>
    public static Dictionary<M2Caller, int> M2(int manager, int counselor, int supervisor, int clientBTeacher) => new()
    {
        [M2Caller.School1Manager] = manager, [M2Caller.School1Counselor] = counselor,
        [M2Caller.School1Supervisor] = supervisor, [M2Caller.ClientBTeacher] = clientBTeacher,
    };
}

/// <summary>The probe milestones. Rows of a closed milestone must be live (their route mapped); later ones may wait.</summary>
public static class ProbeMilestones
{
    /// <summary>The last milestone whose pending rows and known-bug tripwires fail the build. Raise to 3 when M3 closes.</summary>
    public const int LastClosed = 2;
}
