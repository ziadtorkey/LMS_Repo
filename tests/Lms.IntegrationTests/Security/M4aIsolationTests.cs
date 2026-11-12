using System.Reflection;
using Lms.Domain.Common;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Isolation coverage for the M4a tables (M4a plan Task 29, Global Constraints: "an isolation test per business entity"). Each BE PR
/// ships the isolation test of its own tables (client B, sibling, ancestor, no scope, malformed scope, soft delete); this class only
/// guards that none is forgotten: every concrete unit-scoped entity of an M4a domain namespace must have a test method named
/// <c>&lt;Entity&gt;_is_isolated…</c> in this assembly (the convention of M1 and M4a plan Task 3: <c>LessonModule_is_isolated_per_client_and_subtree</c>).
/// </summary>
public sealed class M4aIsolationTests
{
    /// <summary>Domain namespaces whose entities M4a introduces (content, assets, activities, library, forums, grade events).</summary>
    internal static readonly string[] M4aNamespaces =
    [
        "Lms.Domain.Content", "Lms.Domain.Assignments", "Lms.Domain.Quizzes", "Lms.Domain.Library", "Lms.Domain.Forums",
        "Lms.Domain.Grading",
    ];

    [Fact]
    public void Every_unit_scoped_M4a_entity_has_an_isolation_test()
    {
        var entities = typeof(IOrgUnitScoped).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOrgUnitScoped).IsAssignableFrom(t) && InM4a(t.Namespace))
            .ToList();
        var missing = Missing(entities.Select(t => t.Name), TestMethodNames(typeof(M4aIsolationTests).Assembly));
        Assert.True(missing.Count == 0,
            "M4a entities without an isolation test (name it <Entity>_is_isolated_per_client_and_subtree):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void The_guard_finds_a_missing_isolation_test_and_accepts_a_present_one()
    {
        string[] tests = ["LessonModule_is_isolated_per_client_and_subtree", "Quiz_is_isolated_by_client_subtree_and_soft_delete"];
        Assert.Equal(["Asset"], Missing(["LessonModule", "Asset", "Quiz"], tests));
        // A name that merely starts with the entity name is not enough: "Quiz" must not be satisfied by a test of "QuizAttempt".
        Assert.Equal(["QuizAttempt"], Missing(["QuizAttempt"], ["QuizAttempt_helper", "Quiz_is_isolated_per_client_and_subtree"]));
    }

    [Theory]
    [InlineData("Lms.Domain.Content", true)]
    [InlineData("Lms.Domain.Quizzes", true)]
    [InlineData("Lms.Domain.Classes", false)]
    [InlineData("Lms.Domain.ContentX", false)]
    [InlineData(null, false)]
    public void Only_M4a_namespaces_are_in_scope(string? ns, bool expected) => Assert.Equal(expected, InM4a(ns));

    private static bool InM4a(string? ns) =>
        ns is not null && M4aNamespaces.Any(n => ns.Equals(n, StringComparison.Ordinal) || ns.StartsWith(n + ".", StringComparison.Ordinal));

    /// <summary>The entities with no test whose name starts with <c>&lt;Entity&gt;_is_isolated</c>.</summary>
    internal static List<string> Missing(IEnumerable<string> entityNames, IEnumerable<string> testNames)
    {
        var tests = testNames.ToList();
        return entityNames.Where(e => !tests.Any(t => t.StartsWith(e + "_is_isolated", StringComparison.Ordinal))).ToList();
    }

    private static IEnumerable<string> TestMethodNames(Assembly assembly) =>
        assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttributes().Any(a => a is FactAttribute || a is TheoryAttribute))
            .Select(m => m.Name);
}
