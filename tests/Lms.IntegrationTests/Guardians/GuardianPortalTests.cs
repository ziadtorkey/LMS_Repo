using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Features.Guardians;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Me;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Guardians.GuardianTestKit;

namespace Lms.IntegrationTests.Guardians;

[Collection(ApiCollection.Name)]
public sealed class GuardianPortalTests : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly FakeEmailSender _emails = new();
    private readonly WebApplicationFactory<Program> _host;
    private readonly GuardianTestKit _kit;

    public GuardianPortalTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<Lms.Domain.Abstractions.IEmailSender>(_emails)));
        _kit = new GuardianTestKit(factory, _host);
    }

    public void Dispose() => _host.Dispose();

    private async Task SetPortalAsync(AccessTree tree, OrgUnit unit, bool on)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var existing = await db.Set<OrgUnitSetting>().FirstOrDefaultAsync(s => s.OrgUnitId == unit.Id && s.Key == FeatureFlags.GuardianPortal.Name);
        if (existing is null)
        {
            db.Add(new OrgUnitSetting(tree.Org.Id, unit.Id, unit.Path, FeatureFlags.GuardianPortal.Name, on ? "true" : "false", false));
        }
        else
        {
            existing.Change(on ? "true" : "false", false);
        }

        await db.SaveChangesAsync();
    }

    private static GuardianLinkInput WithEmail(string? email, string? nationalId = null, bool primary = true) =>
        new(null, new GuardianDataInput("ولي أمر", "Guardian " + Guid.NewGuid().ToString("N")[..6], UniqueMobile(), email, nationalId), "Father", primary);

    private static string NewEmail() => $"guardian-{Guid.NewGuid():N}@lms.test";

    private async Task<List<RoleAssignment>> GuardianAssignmentsAsync(Guid organizationId, string email)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        if (user is null)
        {
            return [];
        }

        var roleId = (await db.Roles.SingleAsync(r => r.OrganizationId == organizationId && r.BuiltInKey == BuiltInRoles.Guardian)).Id;
        return await db.RoleAssignments.Where(a => a.UserId == user.Id && a.RoleId == roleId).ToListAsync();
    }

    private async Task<HttpResponseMessage> AsGuardianAsync(HttpMethod method, string url, RoleAssignment assignment)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        return await _host.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Linking_guardian_with_email_creates_portal_account_when_flag_on()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP1");
        await SetPortalAsync(tree, tree.Root, true);
        var student = await _kit.StudentAsync(tree.School1);
        var email = NewEmail();

        var link = await _kit.LinkOkAsync(admin, student.Id, WithEmail(email));

        var assignment = Assert.Single(await GuardianAssignmentsAsync(tree.Org.Id, email));
        Assert.Equal(tree.School1.Id, assignment.OrgUnitId);
        var mail = Assert.Single(_emails.To(email));
        Assert.Contains("invite=1", mail.HtmlBody);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var guardian = await db.Set<GuardianProfile>().SingleAsync(g => g.Id == link.GuardianId);
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal(user.Id, guardian.UserId);
        Assert.Null(user.PasswordHash); // activated through the invitation link
    }

    [Fact]
    public async Task No_account_when_GuardianPortal_off_or_no_email()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP2");
        var student = await _kit.StudentAsync(tree.School1);
        var offEmail = NewEmail();

        await _kit.LinkOkAsync(admin, student.Id, WithEmail(offEmail, primary: true)); // the flag is off by default
        await SetPortalAsync(tree, tree.Root, true);
        var other = await _kit.StudentAsync(tree.School1);
        await _kit.LinkOkAsync(admin, other.Id, WithEmail(null, primary: true)); // flag on, but no email

        Assert.Empty(await GuardianAssignmentsAsync(tree.Org.Id, offEmail));
        Assert.Empty(_emails.To(offEmail));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Users.AnyAsync(u => u.Email == offEmail));
    }

    [Fact]
    public async Task Turning_flag_off_then_resync_removes_guardian_assignment()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP3");
        await SetPortalAsync(tree, tree.Root, true);
        var student = await _kit.StudentAsync(tree.School1);
        var email = NewEmail();
        var link = await _kit.LinkOkAsync(admin, student.Id, WithEmail(email));
        Assert.Single(await GuardianAssignmentsAsync(tree.Org.Id, email));

        await SetPortalAsync(tree, tree.Root, false);
        // Any change to the guardian's links or data re-syncs the account.
        var edit = await _kit.SendAsync(HttpMethod.Put, GuardianRoutes.Guardian(link.GuardianId), admin,
            new UpdateGuardianRequest(new GuardianDataInput("ولي", "Edited guardian", UniqueMobile(), email, null)));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        Assert.Empty(await GuardianAssignmentsAsync(tree.Org.Id, email));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.Users.AnyAsync(u => u.Email == email)); // the account is kept
    }

    [Fact]
    public async Task Guardian_of_siblings_in_two_schools_has_two_contexts_and_sees_only_own_children_in_each()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP4");
        await SetPortalAsync(tree, tree.Root, true);
        var firstChild = await _kit.StudentAsync(tree.School1);
        var secondChild = await _kit.StudentAsync(tree.School2);
        var stranger = await _kit.StudentAsync(tree.School1);
        var email = NewEmail();
        var nationalId = UniqueNationalId();
        await _kit.LinkOkAsync(admin, firstChild.Id, WithEmail(email, nationalId));
        await _kit.LinkOkAsync(admin, secondChild.Id, WithEmail(email, nationalId)); // the same guardian (sibling)
        await _kit.LinkOkAsync(admin, stranger.Id, WithEmail(NewEmail()));

        var assignments = await GuardianAssignmentsAsync(tree.Org.Id, email);
        Assert.Equal(new[] { tree.School1.Id, tree.School2.Id }.Order(), assignments.Select(a => a.OrgUnitId).Order());
        var inSchool1 = assignments.Single(a => a.OrgUnitId == tree.School1.Id);
        var inSchool2 = assignments.Single(a => a.OrgUnitId == tree.School2.Id);

        // The chooser lists one context per child unit.
        var me = await (await AsGuardianAsync(HttpMethod.Get, Routes.Me, inSchool1)).Content.ReadFromJsonAsync<MeDto>(Web);
        Assert.Equal(2, me!.Contexts.Count);

        var mine1 = (await (await AsGuardianAsync(HttpMethod.Get, Routes.MyChildren, inSchool1)).Content.ReadFromJsonAsync<List<MyChildDto>>(Web))!;
        var mine2 = (await (await AsGuardianAsync(HttpMethod.Get, Routes.MyChildren, inSchool2)).Content.ReadFromJsonAsync<List<MyChildDto>>(Web))!;
        var child1 = Assert.Single(mine1);
        Assert.Equal((firstChild.Id, "Father", tree.School1.NameEn), (child1.StudentId, child1.Relation, child1.HomeOrgUnitNameEn));
        Assert.Equal(secondChild.Id, Assert.Single(mine2).StudentId);
        Assert.DoesNotContain(mine1.Concat(mine2), c => c.StudentId == stranger.Id);
    }

    [Fact]
    public async Task Linking_from_one_school_keeps_the_contexts_of_the_guardians_other_school()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP5");
        await SetPortalAsync(tree, tree.Root, true);
        var school1Admin = await _kit.CallerWithRoleAsync(tree, tree.School1, BuiltInRoles.OrgAdmin);
        var inSchool2 = await _kit.StudentAsync(tree.School2);
        var inSchool1 = await _kit.StudentAsync(tree.School1);
        var email = NewEmail();
        var nationalId = UniqueNationalId();
        await _kit.LinkOkAsync(admin, inSchool2.Id, WithEmail(email, nationalId));

        // The School 1 admin cannot see the School 2 child, yet linking a sibling must not drop the School 2 context.
        await _kit.LinkOkAsync(school1Admin, inSchool1.Id, WithEmail(email, nationalId));

        var assignments = await GuardianAssignmentsAsync(tree.Org.Id, email);
        Assert.Equal(new[] { tree.School1.Id, tree.School2.Id }.Order(), assignments.Select(a => a.OrgUnitId).Order());
    }

    [Fact]
    public async Task Guardian_cannot_open_other_students_via_students_api_403()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP6");
        await SetPortalAsync(tree, tree.Root, true);
        var child = await _kit.StudentAsync(tree.School1);
        var email = NewEmail();
        await _kit.LinkOkAsync(admin, child.Id, WithEmail(email));
        var guardian = Assert.Single(await GuardianAssignmentsAsync(tree.Org.Id, email));

        Assert.Equal(HttpStatusCode.Forbidden, (await AsGuardianAsync(HttpMethod.Get, Routes.Student(child.Id), guardian)).StatusCode); // even their own child
        Assert.Equal(HttpStatusCode.Forbidden, (await AsGuardianAsync(HttpMethod.Get, Routes.Students, guardian)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AsGuardianAsync(HttpMethod.Get, Routes.MyChildren, guardian)).StatusCode);
    }

    [Fact]
    public async Task Portal_invitation_resend_rules()
    {
        var (tree, admin) = await _kit.SetUpAsync("GP7");
        var student = await _kit.StudentAsync(tree.School1);
        var withEmail = await _kit.LinkOkAsync(admin, student.Id, WithEmail(NewEmail(), primary: true));
        var noEmail = await _kit.LinkOkAsync(admin, (await _kit.StudentAsync(tree.School1)).Id, WithEmail(null, primary: true));
        var outsider = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);

        // No email: 400. Portal off: 409 validation_failed with the detail key. Not visible: 404.
        var missing = await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(noEmail.GuardianId), admin);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var off = await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(withEmail.GuardianId), admin);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        var offBody = await off.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((ProblemCodes.validation_failed, "GuardianPortalDisabled"), (offBody.GetProperty("code").GetString(), offBody.GetProperty("detail").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(withEmail.GuardianId), outsider)).StatusCode);

        // Portal on: the invitation goes out again; once a password is set it is 409 already_activated.
        await SetPortalAsync(tree, tree.Root, true);
        string email;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            email = (await db.Set<GuardianProfile>().SingleAsync(g => g.Id == withEmail.GuardianId)).Email!;
        }

        var sent = await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(withEmail.GuardianId), admin);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        Assert.Single(_emails.To(email));
        Assert.Single(await GuardianAssignmentsAsync(tree.Org.Id, email));
        await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(withEmail.GuardianId), admin);
        Assert.Equal(2, _emails.To(email).Count); // resending sends again while no password is set

        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True((await users.AddPasswordAsync(user, "Passw0rd!x")).Succeeded);
        }

        var activated = await _kit.SendAsync(HttpMethod.Post, Routes.GuardianPortalInvitation(withEmail.GuardianId), admin);
        Assert.Equal(HttpStatusCode.Conflict, activated.StatusCode);
        Assert.Equal(ProblemCodes.already_activated, (await activated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
