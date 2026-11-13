using Lms.Domain.Assignments;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Assignments;

/// <summary>
/// Table-level isolation of the M4a Task 8 tables: another client sees nothing, a sibling unit sees nothing, a parent sees its
/// descendants, no scope sees nothing, soft-deleted rows are hidden from everyone.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AssignmentIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(Assignment Assignment, AssignmentAttachment Attachment, Submission Submission, SubmissionFile File);

    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Rows> AddRowsAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = academic.ClassCourse;
        var stored = new StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "a.pdf", "application/pdf", 1, DateTime.UtcNow);
        var asset = new Asset(cc, stored.Id, AssetKinds.Document, "a.pdf", "application/pdf", 1);
        var assignment = new Assignment(cc);
        assignment.Apply(null, null, null, 10, "Allow", null, "None", true, true);
        var attachment = new AssignmentAttachment(assignment, asset.Id, 1);
        var submission = new Submission(assignment, academic.Student.Id);
        var file = new SubmissionFile(submission, asset.Id);
        db.AddRange(stored, asset, assignment, attachment, submission, file);
        await db.SaveChangesAsync();
        return new Rows(assignment, attachment, submission, file);
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
    public Task Assignment_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<Assignment>("AI1", r => r.Assignment.Id);

    [Fact]
    public Task AssignmentAttachment_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<AssignmentAttachment>("AI2", r => r.Attachment.Id);

    [Fact]
    public Task Submission_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<Submission>("AI3", r => r.Submission.Id);

    [Fact]
    public Task SubmissionFile_is_isolated_per_client_and_subtree() => AssertIsolatedAsync<SubmissionFile>("AI4", r => r.File.Id);
}
