using Lms.Shared;
using Lms.Shared.Messaging;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Targeted announcements (M6 Task 13/14). Announcements are authored at the client root by its Org Admin, so for a school admin, the sibling school's admin
/// and a teacher the authoring unit lies outside their scope (404, never 403); another client never sees it; a platform admin has no client context.
/// The reader routes under <c>/api/me/announcements</c> are self-scoped: they answer only about the caller's own recipient rows.
/// </summary>
public sealed class AnnouncementsProbeRows : IProbeRowModule
{
    private static AnnouncementTargetDto Org() => new(AnnouncementTargetKinds.Organization, null, false);

    private static CreateAnnouncementRequest Create(M1ProbeWorld w, bool publish) =>
        new(w.Callers.A.Root.Id, "عنوان", "Probe " + w.Next(), "<p>نص</p>", "<p>Text</p>", [Org()], [AnnouncementRoleFilters.Students], null, null, false, false, null, publish);

    private static async Task<Guid> NewAsync(M1ProbeWorld w, bool publish) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.OrgAnnouncements, Create(w, publish))).GetProperty("id").GetGuid();

    private static UpdateAnnouncementRequest Update() =>
        new("ع", "Edited", null, "<p>x</p>", [Org()], null, null, null, false, false, null);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("comms announcements capabilities", HttpMethod.Get, Routes.OrgAnnouncementCapabilities,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.OrgAnnouncementCapabilities)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements list", HttpMethod.Get, Routes.OrgAnnouncements,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.OrgAnnouncements)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements create at the root", HttpMethod.Post, Routes.OrgAnnouncements,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgAnnouncements, Create(w, publish: false))),
            Expect(Created, NotFound, NotFound, NotFound, NotFound, NotFound)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements audience preview", HttpMethod.Post, Routes.OrgAnnouncementAudiencePreview,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgAnnouncementAudiencePreview, new AudiencePreviewRequest(w.Callers.A.Root.Id, [Org()], null))),
            Expect(Ok, NotFound, NotFound, NotFound, NotFound, NotFound)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements get", HttpMethod.Get, Routes.OrgAnnouncementPattern,
            async (w, _) => new ProbeRequest(Routes.OrgAnnouncement(await NewAsync(w, false))),
            Expect(Ok, NotFound, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements edit", HttpMethod.Put, Routes.OrgAnnouncementPattern,
            async (w, _) => new ProbeRequest(Routes.OrgAnnouncement(await NewAsync(w, false)), Update()),
            Expect(Ok, NotFound, NotFound, NotFound, NotFound, NotFound)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements delete a draft", HttpMethod.Delete, Routes.OrgAnnouncementPattern,
            async (w, _) => new ProbeRequest(Routes.OrgAnnouncement(await NewAsync(w, false))),
            Expect(NoContent, NotFound, NotFound, NotFound, NotFound, NoContent)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements publish a draft", HttpMethod.Post, Routes.OrgAnnouncementPublishPattern,
            async (w, _) => new ProbeRequest(Routes.OrgAnnouncementPublish(await NewAsync(w, false))),
            Expect(Ok, NotFound, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms announcements withdraw a published one", HttpMethod.Post, Routes.OrgAnnouncementWithdrawPattern,
            async (w, _) => new ProbeRequest(Routes.OrgAnnouncementWithdraw(await NewAsync(w, true))),
            Expect(Ok, NotFound, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms my announcements", HttpMethod.Get, Routes.MyAnnouncements,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyAnnouncements)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)) { Milestone = ProbeMilestones.M6 },
        new("comms my announcement read (not a recipient)", HttpMethod.Post, Routes.MyAnnouncementReadPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyAnnouncementRead(Guid.NewGuid()))),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)) { Milestone = ProbeMilestones.M6 },
    ];
}
