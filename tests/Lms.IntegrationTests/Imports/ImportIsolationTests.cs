using System.Net;
using Lms.Domain.Access;
using Lms.Domain.Imports;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Imports;

/// <summary>Isolation of <see cref="ImportBatch"/> and <see cref="ImportRow"/>: client, subtree, soft delete, endpoints (X-06).</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportIsolationTests(LmsApiFactory factory) : IDisposable
{
    private const string Kind = TestImportDefinition.KindName;
    private readonly ImportTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(Guid Batch, Guid Row)> AddBatchAsync(AccessTree tree, OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var batch = new ImportBatch(unit, Kind, "x.xlsx", Guid.NewGuid(), Guid.NewGuid(), "en");
        db.Add(batch);
        await db.SaveChangesAsync();
        var row = new ImportRow(batch, 2, "{}", null);
        db.Add(row);
        await db.SaveChangesAsync();
        return (batch.Id, row.Id);
    }

    private async Task<List<Guid>> VisibleBatchesAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        return (await db.Set<ImportBatch>().Select(b => b.Id).ToListAsync()).Where(among.Contains).Order().ToList();
    }

    private async Task<List<Guid>> VisibleRowsAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        return (await db.Set<ImportRow>().Select(r => r.Id).ToListAsync()).Where(among.Contains).Order().ToList();
    }

    [Fact]
    public async Task Batches_and_rows_are_isolated_by_client_subtree_and_soft_delete()
    {
        var a = await AccessTestData.SeedAsync(factory, "II1");
        var b = await AccessTestData.SeedAsync(factory, "II2");
        var s1 = await AddBatchAsync(a, a.School1);
        var s2 = await AddBatchAsync(a, a.School2);
        var other = await AddBatchAsync(b, b.Root);
        Guid[] batches = [s1.Batch, s2.Batch, other.Batch];
        Guid[] rows = [s1.Row, s2.Row, other.Row];

        TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

        // another client sees only its own
        Assert.Equal([other.Batch], await VisibleBatchesAsync(In(b, b.Root), batches));
        Assert.Equal([other.Row], await VisibleRowsAsync(In(b, b.Root), rows));
        // siblings see only their own, the client root sees the whole client
        Assert.Equal([s1.Batch], await VisibleBatchesAsync(In(a, a.School1), batches));
        Assert.Equal([s2.Row], await VisibleRowsAsync(In(a, a.School2), rows));
        Assert.Equal(new[] { s1.Batch, s2.Batch }.Order().ToList(), await VisibleBatchesAsync(In(a, a.Root), batches));
        // no scope, no client or a malformed scope path sees nothing
        Assert.Empty(await VisibleBatchesAsync(TestCurrentContext.For(a.Org.Id, null), batches));
        Assert.Empty(await VisibleRowsAsync(new TestCurrentContext { UserId = Guid.NewGuid() }, rows));
        Assert.Empty(await VisibleBatchesAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path[..^1]), batches));
        // the platform admin sees all
        Assert.Equal(batches.Order().ToList(), await VisibleBatchesAsync(TestCurrentContext.PlatformAdmin(), batches));

        // soft delete hides the batch from everyone, its sibling stays
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<ImportBatch>().SingleAsync(x => x.Id == s1.Batch));
            await db.SaveChangesAsync();
        }

        Assert.Equal(new[] { s2.Batch, other.Batch }.Order().ToList(), await VisibleBatchesAsync(TestCurrentContext.PlatformAdmin(), batches));
        Assert.Empty(await VisibleBatchesAsync(In(a, a.School1), batches));
    }

    [Fact]
    public async Task Batch_of_other_client_or_subtree_is_404_on_every_endpoint()
    {
        var a = await AccessTestData.SeedAsync(factory, "II3");
        var b = await AccessTestData.SeedAsync(factory, "II4");
        var school1Admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var clientBAdmin = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var inSchool2 = (await AddBatchAsync(a, a.School2)).Batch;

        foreach (var caller in new[] { school1Admin, clientBAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.ImportBatch(Kind, inSchool2), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, inSchool2), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.ImportErrorReport(Kind, inSchool2), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, inSchool2), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.ImportBatch(Kind, inSchool2), caller)).StatusCode);
            var list = await _host.SendAsync(HttpMethod.Get, Routes.ImportsOfKind(Kind), caller);
            Assert.DoesNotContain(inSchool2.ToString(), await list.Content.ReadAsStringAsync());
        }

        // a batch that does exist for the caller is of course reachable
        var own = (await AddBatchAsync(a, a.School1)).Batch;
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Get, Routes.ImportBatch(Kind, own), school1Admin)).StatusCode);
    }
}
