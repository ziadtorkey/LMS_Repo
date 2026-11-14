using Lms.Domain.Assignments;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Assignments;

/// <summary>Table-level isolation of SubmissionFeedback (M4a Task 9): client, sibling unit, parent, no scope, soft delete.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradingIsolationTests(LmsApiFactory factory)
{
    private static TestCurrentContext In(AccessTree t, OrgUnit u) => TestCurrentContext.For(t.Org.Id, u.Path);

    private async Task<Guid> AddFeedbackAsync(OrgUnit unit)
    {
        var academic = await AcademicTestKit.AddAllAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var assignment = new Assignment(academic.ClassCourse);
        assignment.Apply(null, null, null, 10, "Allow", null, "None", true, true);
        var submission = new Submission(assignment, academic.Student.Id);
        submission.HandIn("<p>x</p>", false, DateTime.UtcNow);
        var feedback = new SubmissionFeedback(submission);
        feedback.Set(5, null, 5, null, null, DateTime.UtcNow);
        db.AddRange(assignment, submission, feedback);
        await db.SaveChangesAsync();
        return feedback.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<SubmissionFeedback>().Select(e => e.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    [Fact]
    public async Task SubmissionFeedback_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "GI1A");
        var b = await AccessTestData.SeedAsync(factory, "GI1B");
        var s1 = await AddFeedbackAsync(a.School1);
        var s2 = await AddFeedbackAsync(a.School2);
        var other = await AddFeedbackAsync(b.Root);
        var atRoot = await AddFeedbackAsync(a.Root);
        Guid[] all = [s1, s2, other, atRoot];

        Assert.True(new[] { other }.SequenceEqual(await VisibleAsync(In(b, b.Root), all)), "other client");
        Assert.True(new[] { s1 }.SequenceEqual(await VisibleAsync(In(a, a.School1), all)), "school 1");
        Assert.True(new[] { s2 }.SequenceEqual(await VisibleAsync(In(a, a.School2), all)), "school 2");
        Assert.True(new[] { s1, s2, atRoot }.Order().SequenceEqual(await VisibleAsync(In(a, a.Root), all)), "parent");
        Assert.Empty(await VisibleAsync(TestCurrentContext.For(a.Org.Id, null), all));
        Assert.Empty(await VisibleAsync(new TestCurrentContext { UserId = Guid.NewGuid() }, all));
        Assert.Equal(all.Length, (await VisibleAsync(TestCurrentContext.PlatformAdmin(), all)).Count);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<SubmissionFeedback>().SingleAsync(e => e.Id == s1));
            await db.SaveChangesAsync();
        }

        Assert.DoesNotContain(s1, await VisibleAsync(In(a, a.Root), all));
    }
}
