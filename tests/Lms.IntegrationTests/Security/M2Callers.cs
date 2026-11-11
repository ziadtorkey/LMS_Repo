using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The staff callers the M2 probes add to the seven M1 callers (M2 plan Task 34). Student and guardian callers arrive
/// with the M3 student routes (D23).
/// </summary>
public enum M2Caller
{
    /// <summary>School Manager of client A at School1: Staff View + Create only (D32), may assign and remove roles.</summary>
    School1Manager,

    /// <summary>Counselor of client A at School1: Staff View only.</summary>
    School1Counselor,

    /// <summary>Supervisor of client A at School1: Staff View only.</summary>
    School1Supervisor,

    /// <summary>Teacher of client B at B's root: staff of another client with no staff permissions.</summary>
    ClientBTeacher,
}

/// <summary>
/// The M2 callers, assigned in the same world as the M1 callers (<see cref="M1ProbeWorld"/>) so a target built for an
/// M1 row is probed by both sets. One instance per test run, like the world.
/// </summary>
public sealed class M2Callers
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static M2Callers? _shared;
    private readonly Dictionary<M2Caller, RoleAssignment> _assignments;

    private M2Callers(M1ProbeWorld world, Dictionary<M2Caller, RoleAssignment> assignments)
    {
        World = world;
        _assignments = assignments;
    }

    public M1ProbeWorld World { get; }

    public static async Task<M2Callers> GetAsync(LmsApiFactory factory)
    {
        var world = await M1ProbeWorld.GetAsync(factory);
        await Gate.WaitAsync();
        try
        {
            if (_shared is null || !ReferenceEquals(_shared.World, world))
            {
                var callers = world.Callers;
                var assignments = new Dictionary<M2Caller, RoleAssignment>
                {
                    [M2Caller.School1Manager] = await AssignAsync(factory, callers.A, callers.A.School1, BuiltInRoles.Manager),
                    [M2Caller.School1Counselor] = await AssignAsync(factory, callers.A, callers.A.School1, BuiltInRoles.Counselor),
                    [M2Caller.School1Supervisor] = await AssignAsync(factory, callers.A, callers.A.School1, BuiltInRoles.Supervisor),
                    [M2Caller.ClientBTeacher] = await AssignAsync(factory, callers.B, callers.B.Root, BuiltInRoles.Teacher),
                };
                _shared = new M2Callers(world, assignments);
            }

            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    public RoleAssignment AssignmentOf(M2Caller caller) => _assignments[caller];

    public Task<HttpResponseMessage> SendAsync(M2Caller caller, HttpMethod method, string url, object? body = null) =>
        World.Callers.SendAsAsync(_assignments[caller], method, url, body);

    private static Task<RoleAssignment> AssignAsync(
        LmsApiFactory factory, AccessTree tree, Lms.Domain.Organizations.OrgUnit unit, string role) =>
        AccessTestData.AssignAsync(factory, tree, unit, role, Guid.NewGuid());
}
