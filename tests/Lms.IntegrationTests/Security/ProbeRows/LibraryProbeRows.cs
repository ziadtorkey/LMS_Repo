using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.Library;
using Lms.Shared;
using Lms.Shared.Library;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Digital library (M4a Task 12, #362). Resources are owned by a unit and readable at that unit and below (read-only below
/// the owner): the get row uses a resource owned at the root, which every client-A caller sees at their own unit, the other client
/// and a platform admin without a client do not. Library.* is not relationship-limited, so the School1 Teacher (Library.Manage at
/// School1) acts like a school admin there. Reading the file and writing are checked at School1.
/// </summary>
public sealed class LibraryProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("library list (School1)", HttpMethod.Get, Routes.Library,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Library + "?orgUnitId=" + w.Callers.A.School1.Id)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("library tags (School1)", HttpMethod.Get, Routes.LibraryTags,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.LibraryTagsOf(w.Callers.A.School1.Id))),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("library create (School1)", HttpMethod.Post, Routes.Library,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Library, LinkBody(w.Callers.A.School1))),
            Expect(Created, Created, NotFound, Created, NotFound, Created)) { Milestone = ProbeMilestones.M4a },
        new("library get (owned at the root)", HttpMethod.Get, Routes.LibraryResourcePattern,
            async (w, _) => new ProbeRequest(Routes.LibraryResource((await NewAsync(w, w.Callers.A.Root)).Id)),
            Expect(Ok, Ok, Ok, Ok, NotFound, NotFound)) { Milestone = ProbeMilestones.M4a },
        new("library update (School1)", HttpMethod.Put, Routes.LibraryResourcePattern,
            async (w, _) => new ProbeRequest(Routes.LibraryResource((await NewAsync(w, w.Callers.A.School1)).Id), LinkBody(w.Callers.A.School1)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("library delete (School1)", HttpMethod.Delete, Routes.LibraryResourcePattern,
            async (w, _) => new ProbeRequest(Routes.LibraryResource((await NewAsync(w, w.Callers.A.School1)).Id)),
            Expect(NoContent, NoContent, NotFound, NoContent, NotFound, NoContent)) { Milestone = ProbeMilestones.M4a },
        new("library reader (School1 file)", HttpMethod.Get, Routes.LibraryResourceReaderPattern,
            async (w, _) => new ProbeRequest(Routes.LibraryResourceReader((await NewFileAsync(w)).Id)),
            Expect(NotFound, Ok, NotFound, Ok, NotFound, NotFound)) { Milestone = ProbeMilestones.M4a },
        new("library download (School1 file)", HttpMethod.Get, Routes.LibraryResourceDownloadPattern,
            async (w, _) => new ProbeRequest(Routes.LibraryResourceDownload((await NewFileAsync(w)).Id)),
            Expect(NotFound, Ok, NotFound, Ok, NotFound, NotFound)) { Milestone = ProbeMilestones.M4a },
    ];

    private static SaveLibraryResourceRequest LinkBody(OrgUnit owner) =>
        new(owner.Id, "مورد", "Probe " + Guid.NewGuid().ToString("N")[..6], null, null, LibraryResourceTypes.Link, [], null, [], null, null,
            "https://example.org/probe", true);

    private static async Task<LibraryResource> NewAsync(M1ProbeWorld w, OrgUnit owner)
    {
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resource = new LibraryResource(owner, "مورد", "Seed", null, null, LibraryResourceTypes.Link, null, null, null, "https://example.org/seed", true);
        db.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }

    private static async Task<LibraryResource> NewFileAsync(M1ProbeWorld w)
    {
        var school1 = w.Callers.A.School1;
        var asset = await LibraryTestWorld.UploadAssetAsync(w.Callers.Factory, w.Callers.A.Root, school1);
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resource = new LibraryResource(school1, "كتاب", "Seed book", null, null, LibraryResourceTypes.Book, null, null, asset, null, true);
        db.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }
}
