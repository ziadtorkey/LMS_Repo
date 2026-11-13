using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// SCORM and H5P packages (M4b Task 2 contract, route table "Packages"). The rows that need no package exist yet: the list of the
/// unattached packages of a class course (<c>Modules.Manage</c> on the class course, the "Checking…" rows of the upload dialog).
/// The upload (multipart, needs the content host configured) and the routes of one package (status, delete, attach, launch,
/// preview, attempt commit and finish) get their rows, with a real package in each unit, when the package store and the host
/// land (Tasks 6 to 9); the derived 403 checks of <see cref="M4bEndpointMatrixTests"/> already cover them by their permission.
/// A class course at School1: School 2 and the other client get 404 (an invisible unit); a Teacher without an assignment to it gets
/// 404 (relationship).
/// </summary>
public sealed class PackagesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("packages of a class course list", HttpMethod.Get, Routes.ClassCoursePackagesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCoursePackages((await LiveSessionsProbeRows.NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];
}
