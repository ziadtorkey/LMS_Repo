using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Announcements;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Announcements;

/// <summary>
/// Who may target what (proposal #613 section 3): <c>Announcements.Manage</c> at the authoring unit, and every target inside the author's reach. A target the
/// author may not reach is a 404, never a 403, so existence is not revealed. A caller whose <c>Announcements.Manage</c> is restricted by a relationship rule (a
/// Teacher: own class courses) may target only own class courses and their groups; the organization, unit, level and class targets, and the urgent flag, need an
/// unrestricted grant.
/// </summary>
public sealed class TargetScopeGuard(LmsDbContext db, IRelationshipScope relationships, IOrgTree orgTree)
{
    public const string Permission = Permissions.Announcements.Manage;

    public async Task<bool> IsUnrestrictedAsync(CancellationToken ct) => (await relationships.ForAsync(Permission, ct)).IsUnrestricted;

    /// <summary>The authoring unit, or the problem to return (404 when the caller holds no <c>Announcements.Manage</c> there).</summary>
    public async Task<(OrgUnit? Unit, IResult? Problem)> AuthoringUnitAsync(HttpContext http, Guid unitId, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permission, unitId, ct);
        return (access.Unit, access.Problem);
    }

    /// <summary>Null when every target is reachable; otherwise the response (404 for an unreachable one).</summary>
    public async Task<IResult?> CheckAsync(HttpContext http, IReadOnlyList<AnnouncementTargetDto> targets, CancellationToken ct)
    {
        var unrestricted = await IsUnrestrictedAsync(ct);
        foreach (var target in targets)
        {
            if (await CheckOneAsync(http, target, unrestricted, ct) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    private async Task<IResult?> CheckOneAsync(HttpContext http, AnnouncementTargetDto target, bool unrestricted, CancellationToken ct)
    {
        var id = target.TargetId;
        switch (target.Kind)
        {
            case AnnouncementTargetKinds.Organization when unrestricted:
                var root = await orgTree.GetRootAsync(http.RequestServices.GetRequiredService<Lms.Domain.Abstractions.ICurrentContext>().OrganizationId ?? Guid.Empty, ct);
                return (await http.CheckUnitAsync(Permission, root.Id, ct)).Problem;
            case AnnouncementTargetKinds.Unit when unrestricted && id is { } unitId:
                return (await http.CheckUnitAsync(Permission, unitId, ct)).Problem;
            case AnnouncementTargetKinds.Level when unrestricted && id is { } levelId:
                return await db.Set<LevelType>().AnyAsync(l => l.Id == levelId, ct) ? null : ApiProblems.NotFound();
            case AnnouncementTargetKinds.Class when unrestricted && id is { } classId:
                return (await http.CheckClassAsync(Permission, classId, ct)).Problem;
            case AnnouncementTargetKinds.ClassCourse when id is { } classCourseId:
                return (await http.CheckClassCourseAsync(Permission, classCourseId, ct)).Problem;
            case AnnouncementTargetKinds.Group when id is { } groupId:
                var classCourse = await db.Set<StudentGroup>().Where(g => g.Id == groupId).Select(g => (Guid?)g.ClassCourseId).FirstOrDefaultAsync(ct);
                return classCourse is { } cc ? (await http.CheckClassCourseAsync(Permission, cc, ct)).Problem : ApiProblems.NotFound();
            default:
                return ApiProblems.NotFound(); // a kind the caller may not use (a Teacher with an organization target) is as invisible as an unreachable one
        }
    }

    /// <summary>The shape rules that do not depend on the caller: kinds known, ids present exactly where needed, one to twenty targets.</summary>
    public static bool IsWellFormed(IReadOnlyList<AnnouncementTargetDto>? targets) =>
        targets is { Count: >= 1 and <= 20 }
        && targets.All(t => AnnouncementTargetKinds.All.Contains(t.Kind)
                            && (t.Kind == AnnouncementTargetKinds.Organization ? t.TargetId is null : t.TargetId is not null)
                            && (!t.IncludeSubtree || t.Kind is AnnouncementTargetKinds.Unit or AnnouncementTargetKinds.Organization));
}
