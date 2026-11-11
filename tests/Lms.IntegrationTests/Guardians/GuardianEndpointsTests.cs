using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Features.Guardians;
using Lms.Domain.Auditing;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Guardians.GuardianTestKit;

namespace Lms.IntegrationTests.Guardians;

/// <summary>M3 Task 19 (#227, FR-STU-06, D30): guardian linking, siblings, primary, mandatory setting, matching and visibility.</summary>
[Collection(ApiCollection.Name)]
public sealed class GuardianEndpointsTests
{
    private readonly LmsApiFactory _factory;
    private readonly GuardianTestKit _kit;

    public GuardianEndpointsTests(LmsApiFactory factory)
    {
        _factory = factory;
        _kit = new GuardianTestKit(factory, factory.WithWebHostBuilder(_ => { }));
    }

    private async Task<int> GuardianRowsAsync(Guid organizationId, string nationalId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GuardianProfile>().IgnoreQueryFilters().CountAsync(g => g.OrganizationId == organizationId && g.NationalId == nationalId);
    }

    [Fact]
    public async Task Two_siblings_share_one_guardian_matched_by_national_id()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD1");
        var first = await _kit.StudentAsync(tree.School1);
        var second = await _kit.StudentAsync(tree.School1);
        var nationalId = UniqueNationalId();

        var a = await _kit.LinkOkAsync(admin, first.Id, NewGuardian(nationalId, primary: true));
        var b = await _kit.LinkOkAsync(admin, second.Id, NewGuardian(nationalId.ToLowerInvariant(), name: "Typed again", primary: true));

        Assert.Equal(a.GuardianId, b.GuardianId); // one guardian for both children
        Assert.Equal(1, await GuardianRowsAsync(tree.Org.Id, nationalId));
        Assert.Equal(1, b.OtherChildrenInScope);
        Assert.Equal("Guardian", a.NameEn[..8]); // the registered data is kept, not overwritten by the second form
    }

    [Fact]
    public async Task Match_returns_masked_mobile_and_no_child_names_from_other_school()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD2");
        var school1Admin = await _kit.CallerWithRoleAsync(tree, tree.School1, BuiltInRoles.OrgAdmin);
        var school2Admin = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);
        var student = await _kit.StudentAsync(tree.School1);
        var nationalId = UniqueNationalId();
        var mobile = "+201001234123";
        await _kit.LinkOkAsync(school1Admin, student.Id, NewGuardian(nationalId, mobile, primary: true, name: "Mona Guardian"));

        var byId = await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId={nationalId}", school2Admin);
        var byMobile = await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?mobile={Uri.EscapeDataString(mobile)}", school2Admin);
        var none = await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId=NOSUCH1234", school2Admin);

        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        var match = (await byId.Content.ReadFromJsonAsync<GuardianMatchDto>(Web))!;
        Assert.Equal("Mona Guardian", match.NameEn);
        Assert.Equal("+*********123", match.MaskedMobile);
        Assert.Equal(1, match.ChildrenCount);
        var raw = await byId.Content.ReadAsStringAsync();
        Assert.DoesNotContain(student.StudentCode, raw);
        Assert.DoesNotContain("Student ", raw);
        Assert.DoesNotContain("201001234", raw);
        Assert.Equal(match.Id, (await byMobile.Content.ReadFromJsonAsync<GuardianMatchDto>(Web))!.Id);
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
        // matching is exact: a partial national id finds nothing
        Assert.Equal(HttpStatusCode.NoContent, (await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId={nationalId[..6]}", school2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.GuardianMatch, school2Admin)).StatusCode);
    }

    [Fact]
    public async Task A_matched_guardian_can_be_linked_by_id_from_another_school()
    {
        var (tree, _) = await _kit.SetUpAsync("GD3");
        var school1Admin = await _kit.CallerWithRoleAsync(tree, tree.School1, BuiltInRoles.OrgAdmin);
        var school2Admin = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);
        var nationalId = UniqueNationalId();
        var origin = await _kit.LinkOkAsync(school1Admin, (await _kit.StudentAsync(tree.School1)).Id, NewGuardian(nationalId, primary: true));
        var sibling = await _kit.StudentAsync(tree.School2);

        var matched = (await (await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId={nationalId}", school2Admin))
            .Content.ReadFromJsonAsync<GuardianMatchDto>(Web))!;
        var linked = await _kit.LinkOkAsync(school2Admin, sibling.Id, new GuardianLinkInput(matched.Id, null, "Mother", true));

        Assert.Equal(origin.GuardianId, linked.GuardianId);
        Assert.Equal(1, await GuardianRowsAsync(tree.Org.Id, nationalId));
    }

    [Fact]
    public async Task Exactly_one_primary_after_link_update_and_unlink()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD4");
        var student = await _kit.StudentAsync(tree.School1);
        var first = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(relation: "Father"));          // none marked: the first becomes primary
        var second = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(relation: "Mother", primary: true)); // a new primary replaces it
        var third = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(relation: "Uncle"));

        Assert.True(first.IsPrimary);
        var links = await _kit.LinksAsync(admin, student.Id);
        Assert.Equal([second.GuardianId], links.Where(l => l.IsPrimary).Select(l => l.GuardianId));

        // switching the primary through update moves the flag
        var switched = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.StudentGuardian(student.Id, third.GuardianId), admin, new UpdateStudentGuardianRequest("Uncle", true));
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal([third.GuardianId], (await _kit.LinksAsync(admin, student.Id)).Where(l => l.IsPrimary).Select(l => l.GuardianId));

        // unsetting the only primary is refused
        var unset = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.StudentGuardian(student.Id, third.GuardianId), admin, new UpdateStudentGuardianRequest("Uncle", false));
        Assert.Equal(HttpStatusCode.Conflict, unset.StatusCode);
        Assert.Equal(ProblemCodes.primary_guardian_required, (await unset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // removing the primary hands over to the oldest remaining link
        var removed = await _kit.SendAsync(HttpMethod.Delete, GuardianRoutes.StudentGuardian(student.Id, third.GuardianId), admin);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var after = await _kit.LinksAsync(admin, student.Id);
        Assert.Equal(2, after.Count);
        Assert.Equal([first.GuardianId], after.Where(l => l.IsPrimary).Select(l => l.GuardianId));
    }

    [Fact]
    public async Task Same_guardian_twice_is_409_guardian_link_exists()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD5");
        var student = await _kit.StudentAsync(tree.School1);
        var nationalId = UniqueNationalId();
        await _kit.LinkOkAsync(admin, student.Id, NewGuardian(nationalId));

        var again = await _kit.SendAsync(HttpMethod.Post, GuardianRoutes.StudentGuardians(student.Id), admin, NewGuardian(nationalId, relation: "Mother"));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ProblemCodes.guardian_link_exists, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cannot_unlink_last_guardian_when_required_at_home_unit()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD6");
        await _kit.SetRequiredAsync(tree, tree.School1, true);
        var student = await _kit.StudentAsync(tree.School1);
        var only = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(primary: true));

        var blocked = await _kit.SendAsync(HttpMethod.Delete, GuardianRoutes.StudentGuardian(student.Id, only.GuardianId), admin);

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(ProblemCodes.guardian_required, (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Single(await _kit.LinksAsync(admin, student.Id));
    }

    [Fact]
    public async Task Required_setting_is_inherited_and_overridable_per_school()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD7");
        await _kit.SetRequiredAsync(tree, tree.Root, true);   // client-wide
        await _kit.SetRequiredAsync(tree, tree.School2, false); // School2 opts out
        var inherited = await _kit.StudentAsync(tree.School1);
        var overridden = await _kit.StudentAsync(tree.School2);
        var g1 = await _kit.LinkOkAsync(admin, inherited.Id, NewGuardian(primary: true));
        var g2 = await _kit.LinkOkAsync(admin, overridden.Id, NewGuardian(primary: true));

        var school1 = await _kit.SendAsync(HttpMethod.Delete, GuardianRoutes.StudentGuardian(inherited.Id, g1.GuardianId), admin);
        var school2 = await _kit.SendAsync(HttpMethod.Delete, GuardianRoutes.StudentGuardian(overridden.Id, g2.GuardianId), admin);

        Assert.Equal(HttpStatusCode.Conflict, school1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, school2.StatusCode);
    }

    [Fact]
    public async Task School2_admin_gets_404_for_guardian_linked_only_in_School1()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD8");
        var school1Admin = await _kit.CallerWithRoleAsync(tree, tree.School1, BuiltInRoles.OrgAdmin);
        var school2Admin = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);
        var student = await _kit.StudentAsync(tree.School1);
        var link = await _kit.LinkOkAsync(school1Admin, student.Id, NewGuardian(primary: true));

        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), school2Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(link.GuardianId), school2Admin,
            new UpdateGuardianRequest(new GuardianDataInput("س", "Edited", UniqueMobile(), null, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.StudentGuardians(student.Id), school2Admin)).StatusCode);
        var list = (await (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardians, school2Admin))
            .Content.ReadFromJsonAsync<PagedResult<GuardianSummaryDto>>(Web))!;
        Assert.DoesNotContain(list.Items, g => g.Id == link.GuardianId);
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), admin)).StatusCode);
    }

    [Fact]
    public async Task Guardian_shared_by_siblings_in_both_schools_is_visible_to_both_with_own_children_only()
    {
        var (tree, _) = await _kit.SetUpAsync("GD9");
        var school1Admin = await _kit.CallerWithRoleAsync(tree, tree.School1, BuiltInRoles.OrgAdmin);
        var school2Admin = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);
        var child1 = await _kit.StudentAsync(tree.School1);
        var child2 = await _kit.StudentAsync(tree.School2);
        var nationalId = UniqueNationalId();
        var link = await _kit.LinkOkAsync(school1Admin, child1.Id, NewGuardian(nationalId, primary: true));
        await _kit.LinkOkAsync(school2Admin, child2.Id, new GuardianLinkInput(link.GuardianId, null, "Mother", true));

        var seenBy1 = (await (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), school1Admin)).Content.ReadFromJsonAsync<GuardianDetailDto>(Web))!;
        var seenBy2 = (await (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), school2Admin)).Content.ReadFromJsonAsync<GuardianDetailDto>(Web))!;

        Assert.Equal([child1.Id], seenBy1.Children.Select(c => c.StudentId));
        Assert.Equal([child2.Id], seenBy2.Children.Select(c => c.StudentId));
        Assert.Equal(1, seenBy1.Summary.ChildrenInScope);
        // any school with a child of the guardian may edit the shared guardian (D30)
        var edit = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(link.GuardianId), school2Admin,
            new UpdateGuardianRequest(new GuardianDataInput("ولي", "Edited By School2", UniqueMobile(), null, nationalId)));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task Guardian_edit_with_another_guardians_national_id_or_mobile_is_409_with_field_only()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD10");
        var student = await _kit.StudentAsync(tree.School1);
        var other = await _kit.StudentAsync(tree.School1);
        var firstNid = UniqueNationalId();
        var firstMobile = UniqueMobile();
        await _kit.LinkOkAsync(admin, other.Id, NewGuardian(firstNid, firstMobile, primary: true));
        var mine = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(UniqueNationalId(), primary: true));

        var byNid = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(mine.GuardianId), admin, new UpdateGuardianRequest(new GuardianDataInput("س", "Same", UniqueMobile(), null, firstNid)));
        var byMobile = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(mine.GuardianId), admin, new UpdateGuardianRequest(new GuardianDataInput("س", "Same", firstMobile, null, UniqueNationalId())));
        var self = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(mine.GuardianId), admin, new UpdateGuardianRequest(new GuardianDataInput("س", "Renamed", mine.Mobile, null, mine.NationalId)));

        Assert.Equal(HttpStatusCode.Conflict, byNid.StatusCode);
        var body = await byNid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.duplicate_person, body.GetProperty("code").GetString());
        Assert.Equal("nationalId", body.GetProperty("field").GetString());
        Assert.Equal("mobile", (await byMobile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.OK, self.StatusCode); // its own values are not a duplicate
    }

    [Fact]
    public async Task Client_B_cannot_match_or_see_client_A_guardian()
    {
        var (a, adminA) = await _kit.SetUpAsync("GD11A");
        var (_, adminB) = await _kit.SetUpAsync("GD11B");
        var nationalId = UniqueNationalId();
        var link = await _kit.LinkOkAsync(adminA, (await _kit.StudentAsync(a.School1)).Id, NewGuardian(nationalId, primary: true));

        Assert.Equal(HttpStatusCode.NoContent, (await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId={nationalId}", adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), adminB)).StatusCode);
        // linking by the id of another client's guardian is refused as if it did not exist
        var intruder = await _kit.StudentAsync((await _kit.SetUpAsync("GD11C")).Tree.School1);
        var other = await _kit.SetUpAsync("GD11D");
        var foreign = await _kit.StudentAsync(other.Tree.School1);
        var response = await _kit.SendAsync(HttpMethod.Post, GuardianRoutes.StudentGuardians(foreign.Id), other.Admin, new GuardianLinkInput(link.GuardianId, null, "Father", true));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(intruder);
    }

    [Fact]
    public async Task Link_and_edit_are_audited()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD12");
        var student = await _kit.StudentAsync(tree.School1);
        var link = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(primary: true));
        await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(link.GuardianId), admin,
            new UpdateGuardianRequest(new GuardianDataInput("ولي", "Audited Name", link.Mobile, null, null)));

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.IgnoreQueryFilters().Where(e => e.OrganizationId == tree.Org.Id).ToListAsync();
        Assert.Contains(entries, e => e.EntityType == nameof(StudentGuardian) && e.Action == AuditAction.Created && e.AfterJson!.Contains(link.GuardianId.ToString()));
        Assert.Contains(entries, e => e.EntityType == nameof(GuardianProfile) && e.Action == AuditAction.Created);
        Assert.Contains(entries, e => e.EntityType == nameof(GuardianProfile) && e.Action == AuditAction.Updated && e.AfterJson!.Contains("Audited Name"));
    }

    [Fact]
    public async Task Supervisor_can_view_but_not_link_403()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD13");
        var student = await _kit.StudentAsync(tree.School1);
        var link = await _kit.LinkOkAsync(admin, student.Id, NewGuardian(primary: true));
        var supervisor = await _kit.CallerWithRoleAsync(tree, tree.Root, BuiltInRoles.Supervisor);

        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.StudentGuardians(student.Id), supervisor)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.Guardian(link.GuardianId), supervisor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Post, GuardianRoutes.StudentGuardians(student.Id), supervisor, NewGuardian())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.StudentGuardian(student.Id, link.GuardianId), supervisor, new UpdateStudentGuardianRequest("Father", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Delete, GuardianRoutes.StudentGuardian(student.Id, link.GuardianId), supervisor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Get, $"{GuardianRoutes.GuardianMatch}?nationalId=X12345", supervisor)).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_400_and_unknown_student_is_404()
    {
        var (tree, admin) = await _kit.SetUpAsync("GD14");
        var student = await _kit.StudentAsync(tree.School1);
        var url = GuardianRoutes.StudentGuardians(student.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, url, admin, NewGuardian(mobile: "12"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, url, admin, NewGuardian(nationalId: "no spaces!"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, url, admin, NewGuardian(relation: "Cousin"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, url, admin, new GuardianLinkInput(null, null, "Father", true))).StatusCode); // neither
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, url, admin,
            new GuardianLinkInput(Guid.NewGuid(), new GuardianDataInput("س", "Both", UniqueMobile(), null, null), "Father", true))).StatusCode); // both
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, url, admin, new GuardianLinkInput(Guid.NewGuid(), null, "Father", true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, GuardianRoutes.StudentGuardians(Guid.NewGuid()), admin)).StatusCode);
        Assert.Empty(await _kit.LinksAsync(admin, student.Id));
    }
}
