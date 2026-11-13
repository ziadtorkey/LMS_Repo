using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// A test class that keeps a host (a <c>WebApplicationFactory</c> or a test host wrapping one) in a field must be disposable and dispose
/// it. A host that is never disposed stays in memory, with its whole container, until the end of the run: that took the full suite past
/// 13 GB and made CI look hung (#466, #618). <see cref="DerivedHostCleanupAttribute"/> is the safety net; this guard keeps the cause out.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HostDisposalGuardTests(LmsApiFactory factory)
{
    /// <summary>The shared fixture itself is owned by xUnit.</summary>
    private static readonly HashSet<Type> Owned = [typeof(LmsApiFactory)];

    private static bool HoldsAHost(Type fieldType) =>
        !Owned.Contains(fieldType)
        && (typeof(WebApplicationFactory<Program>).IsAssignableFrom(fieldType)
            || (typeof(IDisposable).IsAssignableFrom(fieldType) && fieldType.Name.EndsWith("TestHost", StringComparison.Ordinal))
            || (typeof(IDisposable).IsAssignableFrom(fieldType) && fieldType.Name.EndsWith("TestKit", StringComparison.Ordinal))
            || (typeof(IDisposable).IsAssignableFrom(fieldType) && fieldType.Name.EndsWith("World", StringComparison.Ordinal)));

    private static bool IsDisposable(Type type) =>
        typeof(IDisposable).IsAssignableFrom(type) || typeof(IAsyncDisposable).IsAssignableFrom(type);

    [Fact]
    public void Every_class_that_holds_a_host_in_a_field_is_disposable()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var offenders = typeof(HostDisposalGuardTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsNested && !IsDisposable(t) && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .Where(t => t.GetFields(all).Any(f => HoldsAHost(f.FieldType)))
            .Select(t => t.FullName)
            .Order()
            .ToList();

        Assert.True(offenders.Count == 0,
            "These classes keep a host in a field but are not IDisposable (implement Dispose and dispose the host):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_derived_host_created_by_a_test_is_disposed_after_the_test()
    {
        // The hook that backs the guard above: nothing the previous test created survives it.
        // (This test itself creates none, so after it the tracked set must be empty.)
        factory.DisposeDerivedHosts();
        Assert.Equal(0, factory.TrackedDerivedHosts);
    }
}
