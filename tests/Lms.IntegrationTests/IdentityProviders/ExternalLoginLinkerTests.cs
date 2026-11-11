using System.Security.Claims;
using Lms.Domain.Identity;
using Lms.Domain.IdentityProviders;
using Lms.Domain.People;
using Lms.Infrastructure.Identity.External;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class ExternalLoginLinkerTests(LmsApiFactory factory)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private static ClaimsPrincipal External(string sub, params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            new[] { new Claim("sub", sub) }.Concat(claims.Select(c => new Claim(c.Type, c.Value))), "external"));

    private async Task<IdentityProviderConfig> ProviderAsync(
        Guid organizationId, IdentityProviderKind kind = IdentityProviderKind.Oidc,
        ExternalMatchBy matchBy = ExternalMatchBy.Email, string? nationalIdClaim = null)
    {
        var provider = new IdentityProviderConfig(
            organizationId, kind, Unique("lnk-"), "مزود", "Provider", "https://idp.example.test", null, "cid", null,
            "openid email", matchBy, nationalIdClaim, "school.example", enabled: true);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Set<IdentityProviderConfig>().Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    private async Task<User> UserAsync(string email, bool platformAdmin = false)
    {
        using var scope = factory.Services.CreateScope();
        var user = new User { UserName = email, Email = email, DisplayNameEn = "Person", IsPlatformAdmin = platformAdmin };
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user);
        Assert.True(result.Succeeded);
        return user;
    }

    private async Task<StaffProfile> StaffAsync(AccessTree tree, User user, string? nationalId = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var unit = await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id);
        var staff = new StaffProfile(unit, user.Id, "اسم", "Name", user.Email!, null, nationalId, null, null, null, null);
        db.Set<StaffProfile>().Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    private async Task<ExternalLinkResult> ResolveAsync(IdentityProviderConfig provider, ClaimsPrincipal principal)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalLoginLinker>().ResolveAsync(provider, principal, CancellationToken.None);
    }

    [Fact]
    public async Task First_login_matches_staff_by_email_and_links()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL1");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("a") + "@school.example";
        var user = await UserAsync(email);
        await StaffAsync(tree, user);
        var sub = Unique("sub-");

        var result = await ResolveAsync(provider, External(sub, ("email", email.ToUpperInvariant())));

        Assert.Equal(user.Id, result.User!.Id);
        Assert.Null(result.ErrorCode);
        using var scope = factory.Services.CreateScope();
        var linked = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
            .FindByLoginAsync(ExternalSchemeRegistry.SchemeName(provider.Id), sub);
        Assert.Equal(user.Id, linked!.Id);
    }

    [Fact]
    public async Task Second_login_uses_link_even_if_email_changed_at_provider()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL2");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("b") + "@school.example";
        var user = await UserAsync(email);
        await StaffAsync(tree, user);
        var sub = Unique("sub-");
        await ResolveAsync(provider, External(sub, ("email", email)));

        var second = await ResolveAsync(provider, External(sub, ("email", "renamed@school.example")));

        Assert.Equal(user.Id, second.User!.Id);
    }

    [Fact]
    public async Task Azure_provider_falls_back_to_preferred_username()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL3");
        var provider = await ProviderAsync(tree.Org.Id, IdentityProviderKind.AzureAd);
        var email = Unique("c") + "@school.example";
        var user = await UserAsync(email);
        await StaffAsync(tree, user);

        var result = await ResolveAsync(provider, External(Unique("sub-"), ("preferred_username", email)));

        Assert.Equal(user.Id, result.User!.Id);
    }

    [Fact]
    public async Task National_id_match_links_student()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL4");
        var provider = await ProviderAsync(tree.Org.Id, matchBy: ExternalMatchBy.NationalId, nationalIdClaim: "nid");
        var user = await UserAsync(Unique("s") + "@students.example");
        var nationalId = "N" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id);
            var student = new StudentProfile(unit, Unique("S"), "طالب", "Student", new DateOnly(2012, 1, 1), "M", "R", "EG");
            student.Update("طالب", "Student", null, null, new DateOnly(2012, 1, 1), "M", "R", "EG", nationalId, null, null, null);
            student.LinkUser(user.Id);
            db.Set<StudentProfile>().Add(student);
            await db.SaveChangesAsync();
        }

        var result = await ResolveAsync(provider, External(Unique("sub-"), ("nid", nationalId.ToLowerInvariant())));

        Assert.Equal(user.Id, result.User!.Id);
    }

    [Fact]
    public async Task Two_people_with_same_national_id_is_ambiguous()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL5");
        var provider = await ProviderAsync(tree.Org.Id, matchBy: ExternalMatchBy.NationalId, nationalIdClaim: "nid");
        var nationalId = "D" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await StaffAsync(tree, await UserAsync(Unique("d1") + "@school.example"), nationalId);
        await StaffAsync(tree, await UserAsync(Unique("d2") + "@school.example"), nationalId);

        var result = await ResolveAsync(provider, External(Unique("sub-"), ("nid", nationalId)));

        Assert.Null(result.User);
        Assert.Equal("external_account_ambiguous", result.ErrorCode);
    }

    [Fact]
    public async Task Email_of_user_without_profile_in_this_client_is_not_found()
    {
        var a = await AccessTestData.SeedAsync(factory, "ELL6A");
        var b = await AccessTestData.SeedAsync(factory, "ELL6B");
        var provider = await ProviderAsync(a.Org.Id);
        var email = Unique("e") + "@school.example";
        var user = await UserAsync(email);
        await StaffAsync(b, user); // a person of client B only

        var result = await ResolveAsync(provider, External(Unique("sub-"), ("email", email)));

        Assert.Null(result.User);
        Assert.Equal("external_account_not_found", result.ErrorCode);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
            .FindByLoginAsync(ExternalSchemeRegistry.SchemeName(provider.Id), "x"));
    }

    [Fact]
    public async Task Unknown_identity_is_not_found_and_nothing_is_created()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL7");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("nobody") + "@school.example";

        var result = await ResolveAsync(provider, External(Unique("sub-"), ("email", email)));

        Assert.Equal("external_account_not_found", result.ErrorCode);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email));
    }

    [Fact]
    public async Task Success_and_failure_write_security_events()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL8");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("f") + "@school.example";
        var user = await UserAsync(email);
        await StaffAsync(tree, user);

        await ResolveAsync(provider, External(Unique("sub-"), ("email", email)));
        await ResolveAsync(provider, External(Unique("sub-"), ("email", "ghost@school.example")));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.IgnoreQueryFilters().Where(e => e.EntityType.StartsWith("ExternalLogin")).ToListAsync();
        Assert.Contains(entries, e => e.EntityType == "ExternalLoginLinked" && e.UserId == user.Id);
        Assert.Contains(entries, e => e.EntityType == "ExternalLoginSucceeded" && e.UserId == user.Id);
        Assert.Contains(entries, e => e.EntityType == "ExternalLoginFailed" && e.UserId == null
            && e.AfterJson!.Contains(provider.Slug) && e.AfterJson.Contains("external_account_not_found"));
    }

    [Fact]
    public async Task National_id_of_a_person_in_another_client_is_not_found_and_not_ambiguous()
    {
        var a = await AccessTestData.SeedAsync(factory, "ELL9A");
        var b = await AccessTestData.SeedAsync(factory, "ELL9B");
        var provider = await ProviderAsync(a.Org.Id, matchBy: ExternalMatchBy.NationalId, nationalIdClaim: "nid");
        var nationalId = "X" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await StaffAsync(b, await UserAsync(Unique("g1") + "@school.example"), nationalId); // same id, other client

        var onlyOther = await ResolveAsync(provider, External(Unique("sub-"), ("nid", nationalId)));
        Assert.Equal("external_account_not_found", onlyOther.ErrorCode);

        // one match in client A plus one in client B is a single match, not an ambiguity
        var mine = await UserAsync(Unique("g2") + "@school.example");
        await StaffAsync(a, mine, nationalId);
        var result = await ResolveAsync(provider, External(Unique("sub-"), ("nid", nationalId)));
        Assert.Equal(mine.Id, result.User!.Id);
    }

    [Fact]
    public async Task Platform_admin_is_refused_with_the_generic_message_and_an_audited_reason()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL10");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("pa") + "@school.example";
        var admin = await UserAsync(email, platformAdmin: true);
        await StaffAsync(tree, admin); // a platform admin who also has a profile in this client
        var sub = Unique("sub-");

        var result = await ResolveAsync(provider, External(sub, ("email", email)));

        Assert.Null(result.User);
        Assert.Equal("external_account_not_found", result.ErrorCode);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<Lms.Domain.Identity.User>>()
            .FindByLoginAsync(ExternalSchemeRegistry.SchemeName(provider.Id), sub)); // nothing was linked
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Contains(await db.AuditEntries.IgnoreQueryFilters().Where(e => e.EntityType == "ExternalLoginFailed" && e.UserId == admin.Id).ToListAsync(),
            e => e.AfterJson!.Contains("platform_admin"));
    }

    [Fact]
    public async Task Existing_link_is_refused_once_the_person_left_the_client()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ELL11");
        var provider = await ProviderAsync(tree.Org.Id);
        var email = Unique("gone") + "@school.example";
        var user = await UserAsync(email);
        var staff = await StaffAsync(tree, user);
        var sub = Unique("sub-");
        Assert.NotNull((await ResolveAsync(provider, External(sub, ("email", email)))).User);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = await db.Set<StaffProfile>().SingleAsync(p => p.Id == staff.Id);
            db.Remove(row); // soft delete: no longer a member of the client
            await db.SaveChangesAsync();
        }

        var again = await ResolveAsync(provider, External(sub, ("email", email)));

        Assert.Null(again.User);
        Assert.Equal("external_account_not_found", again.ErrorCode);
    }
}
