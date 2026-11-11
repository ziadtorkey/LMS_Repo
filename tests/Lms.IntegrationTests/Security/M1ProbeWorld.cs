using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Security;

/// <summary>The seeded callers plus helpers that create fresh targets for one probe call.</summary>
public sealed class M1ProbeWorld(M1Callers callers)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static M1ProbeWorld? _shared;
    private int _sequence;

    public M1Callers Callers { get; } = callers;

    /// <summary>One world per test run: seeding seven callers for each of a few hundred probe cases would be slow.</summary>
    public static async Task<M1ProbeWorld> GetAsync(LmsApiFactory factory)
    {
        await Gate.WaitAsync();
        try
        {
            if (_shared is null || !ReferenceEquals(_shared.Callers.Factory, factory))
            {
                var callers = new M1Callers(factory);
                await callers.InitializeAsync("M1P");
                _shared = new M1ProbeWorld(callers);
            }

            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>A number unique within the run, for names, codes and non-overlapping dates.</summary>
    public int Next() => Interlocked.Increment(ref _sequence);

    /// <summary>
    /// A fresh unit below <paramref name="parent"/> of client A, created directly in the database (the org-unit API may not
    /// be merged yet). Each probe call works on its own unit, so per-unit rules (overlapping years, one Active year) never
    /// make one caller's expected status depend on another caller's call.
    /// </summary>
    public async Task<OrgUnit> NewUnitAsync(OrgUnit parent)
    {
        var n = Next();
        await using var db = Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var levelTypeId = parent.LevelTypeId;
        var unit = OrgUnit.CreateChild(parent, levelTypeId, "وحدة " + n, "Probe unit " + n, "P" + n);
        db.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }
}
