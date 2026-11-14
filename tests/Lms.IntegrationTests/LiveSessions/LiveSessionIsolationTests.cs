using Lms.Domain.LiveSessions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>
/// Table-level isolation of the M4b Task 4 tables: another client sees nothing, a sibling unit sees nothing, a parent sees its
/// descendants, no scope sees nothing, soft-deleted rows are hidden from everyone.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LiveSessionIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(LiveSessionSeries Series, LiveSession Session, LiveSessionParticipant Participant);

    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Rows> AddRowsAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = academic.ClassCourse;
        var lesson = Guid.NewGuid();
        var series = new LiveSessionSeries(cc, lesson, new RecurrenceRule(1, ["Sunday"], null, 2, "UTC"));
        var session = new LiveSession(cc, lesson, series.Id, 1, "Fake", DateTime.UtcNow.AddDays(1), 60);
        var participant = new LiveSessionParticipant(session, cc, "Guest", LiveSessionRules.Guest, null, DateTime.UtcNow, null, 0);
        db.AddRange(series, session, participant);
        await db.SaveChangesAsync();
        return new Rows(series, session, participant);
    }

    private async Task<List<Guid>> VisibleAsync<T>(TestCurrentContext who, params Guid[] among) where T : class
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<T>().Select(e => EF.Property<Guid>(e, "Id")).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private async Task AssertIsolatedAsync<T>(string name, Func<Rows, Guid> id) where T : class
    {
        var a = await AccessTestData.SeedAsync(factory, name + "A");
        var b = await AccessTestData.SeedAsync(factory, name + "B");
        var s1 = await AddRowsAsync(a.School1);
        var s2 = await AddRowsAsync(a.School2);
        var other = await AddRowsAsync(b.Root);
        var atRoot = await AddRowsAsync(a.Root);
        Guid[] all = [id(s1), id(s2), id(other), id(atRoot)];

        Assert.True(new[] { id(other) }.SequenceEqual(await VisibleAsync<T>(In(b, b.Root), all)), "other client");
        Assert.True(new[] { id(s1) }.SequenceEqual(await VisibleAsync<T>(In(a, a.School1), all)), "school 1");
        Assert.True(new[] { id(s2) }.SequenceEqual(await VisibleAsync<T>(In(a, a.School2), all)), "school 2");
        Assert.True(
            new[] { id(s1), id(s2), id(atRoot) }.Order().SequenceEqual(await VisibleAsync<T>(In(a, a.Root), all)), "parent");
        Assert.Empty(await VisibleAsync<T>(TestCurrentContext.For(a.Org.Id, null), all));
        Assert.Empty(await VisibleAsync<T>(new TestCurrentContext { UserId = Guid.NewGuid() }, all));
        Assert.Empty(await VisibleAsync<T>(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1]), all));
        Assert.Equal(all.Length, (await VisibleAsync<T>(TestCurrentContext.PlatformAdmin(), all)).Count);

        // A soft-deleted row is hidden from everyone but the platform admin's raw view.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = await db.Set<T>().SingleAsync(e => EF.Property<Guid>(e, "Id") == id(s1));
            db.Remove(row);
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain(id(s1), await VisibleAsync<T>(In(a, a.Root), all));
    }

    [Fact]
    public Task LiveSessionSeries_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<LiveSessionSeries>("LI1", r => r.Series.Id);

    [Fact]
    public Task LiveSession_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<LiveSession>("LI2", r => r.Session.Id);

    [Fact]
    public Task LiveSessionParticipant_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<LiveSessionParticipant>("LI3", r => r.Participant.Id);
}
