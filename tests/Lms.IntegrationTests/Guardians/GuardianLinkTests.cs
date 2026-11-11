using Lms.Shared;
using System.Collections.Concurrent;
using System.Net;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Guardians.GuardianTestKit;

namespace Lms.IntegrationTests.Guardians;

[Collection(ApiCollection.Name)]
public sealed class GuardianLinkTests(LmsApiFactory factory)
{
    /// <summary>Records each notification together with what a separate connection can already read at that moment.</summary>
    private sealed class RecordingObserver(LmsApiFactory factory) : IGuardianLinkObserver
    {
        public ConcurrentQueue<(Guid GuardianId, int LiveLinksVisibleToOthers)> Calls { get; } = new();

        public async Task OnLinksChangedAsync(GuardianProfile guardian, CancellationToken cancellationToken)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var links = await db.Set<StudentGuardian>().IgnoreQueryFilters().CountAsync(l => l.GuardianId == guardian.Id && !l.IsDeleted, cancellationToken);
            Calls.Enqueue((guardian.Id, links));
        }
    }

    [Fact]
    public async Task Observer_is_called_after_commit_on_link_and_unlink()
    {
        var observer = new RecordingObserver(factory);
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IGuardianLinkObserver>(observer)));
        var kit = new GuardianTestKit(factory, host);
        var (tree, admin) = await kit.SetUpAsync("GL1");
        var student = await kit.StudentAsync(tree.School1);

        var link = await kit.LinkOkAsync(admin, student.Id, NewGuardian(primary: true));
        var linked = Assert.Single(observer.Calls);
        Assert.Equal((link.GuardianId, 1), linked); // the link was already committed when the observer ran

        var removed = await kit.SendAsync(HttpMethod.Delete, Routes.StudentGuardian(student.Id, link.GuardianId), admin);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(2, observer.Calls.Count);
        Assert.Equal((link.GuardianId, 0), observer.Calls.Last());
    }

    [Fact]
    public async Task Observer_is_also_told_about_primary_changes_and_guardian_edits()
    {
        var observer = new RecordingObserver(factory);
        var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IGuardianLinkObserver>(observer)));
        var kit = new GuardianTestKit(factory, host);
        var (tree, admin) = await kit.SetUpAsync("GL2");
        var student = await kit.StudentAsync(tree.School1);
        var first = await kit.LinkOkAsync(admin, student.Id, NewGuardian(primary: true));
        var second = await kit.LinkOkAsync(admin, student.Id, NewGuardian());
        observer.Calls.Clear();

        await kit.SendAsync(HttpMethod.Put, Routes.StudentGuardian(student.Id, second.GuardianId), admin, new UpdateStudentGuardianRequest("Mother", true));
        var afterPrimary = observer.Calls.Select(c => c.GuardianId).Distinct().ToList();
        observer.Calls.Clear();
        await kit.SendAsync(HttpMethod.Put, Routes.Guardian(first.GuardianId), admin, new UpdateGuardianRequest(new GuardianDataInput("ولي", "New Name", first.Mobile, null, null)));

        Assert.Contains(second.GuardianId, afterPrimary);
        Assert.Contains(first.GuardianId, observer.Calls.Select(c => c.GuardianId));
    }
}
