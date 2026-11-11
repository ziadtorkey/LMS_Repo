using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using IdentityProviderConfig = Lms.Domain.IdentityProviders.IdentityProviderConfig;
using Lms.Infrastructure.Identity.External;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.IdentityProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class IdentityProviderEndpointsTests(LmsApiFactory factory)
{
    private const string Secret = "very-secret-client-value";

    private static string NewSlug() => "idp-" + Guid.NewGuid().ToString("N")[..10];

    private static SaveIdentityProviderRequest Azure(string slug, string? secret = Secret, string tenant = "contoso-tenant-id") => new(
        IdentityProviderKinds.AzureAd, slug, "مزود", "Provider", null, tenant, "client-id", secret,
        "openid profile email", ExternalMatchBy.Email, null, ["School.Example"], true);

    private static SaveIdentityProviderRequest Oidc(string slug, string authority = "https://127.0.0.1:1") => new(
        IdentityProviderKinds.Oidc, slug, "مزود", "Provider", authority, null, "client-id", Secret,
        "openid profile email", ExternalMatchBy.Email, null, ["school.example"], true);

    private async Task<RoleAssignment> RootAdminAsync(string prefix)
    {
        var tree = await AccessTestData.SeedAsync(factory, prefix);
        return await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
    }

    private async Task<IdentityProviderDto> CreateAsync(RoleAssignment admin, SaveIdentityProviderRequest body)
    {
        var response = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdentityProviderDto>(Web))!;
    }

    [Fact]
    public async Task Root_admin_creates_azure_provider_and_authority_is_derived()
    {
        var admin = await RootAdminAsync("IP1");
        var slug = NewSlug();

        var created = await CreateAsync(admin, Azure(slug));

        Assert.Equal("https://login.microsoftonline.com/contoso-tenant-id/v2.0", created.Authority);
        Assert.Equal(slug, created.Slug);
        Assert.Equal(["school.example"], created.EmailDomains);
        Assert.True(created.HasClientSecret);
        Assert.EndsWith($"/signin-oidc/{slug}", created.CallbackUrl);
        var listed = (await (await SendAsync(factory, HttpMethod.Get, Routes.IdentityProviders, admin))
            .Content.ReadFromJsonAsync<List<IdentityProviderDto>>(Web))!;
        Assert.Contains(listed, p => p.Id == created.Id);
    }

    [Fact]
    public async Task Secret_is_never_returned_and_is_stored_protected()
    {
        var admin = await RootAdminAsync("IP2");
        var created = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin, Azure(NewSlug()));
        var json = await created.Content.ReadAsStringAsync();
        var id = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("id").GetGuid();

        Assert.DoesNotContain(Secret, json);
        var list = await (await SendAsync(factory, HttpMethod.Get, Routes.IdentityProviders, admin)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, list);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var stored = await db.Set<IdentityProviderConfig>().IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        Assert.NotNull(stored.ProtectedClientSecret);
        Assert.NotEqual(Secret, stored.ProtectedClientSecret);
        Assert.Equal(Secret, factory.Services.GetRequiredService<ProviderSecretProtector>().Unprotect(stored.ProtectedClientSecret!));
    }

    [Fact]
    public async Task Update_without_secret_keeps_existing_secret()
    {
        var admin = await RootAdminAsync("IP3");
        var created = await CreateAsync(admin, Azure(NewSlug()));

        var put = await SendAsync(factory, HttpMethod.Put, Routes.IdentityProvider(created.Id), admin,
            Azure(created.Slug, secret: null) with { DisplayNameEn = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = (await put.Content.ReadFromJsonAsync<IdentityProviderDto>(Web))!;
        Assert.Equal("Renamed", dto.DisplayNameEn);
        Assert.True(dto.HasClientSecret);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var stored = await db.Set<IdentityProviderConfig>().IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id);
        Assert.Equal(Secret, factory.Services.GetRequiredService<ProviderSecretProtector>().Unprotect(stored.ProtectedClientSecret!));
    }

    [Fact]
    public async Task Slug_taken_by_another_client_is_409()
    {
        var a = await RootAdminAsync("IP4A");
        var b = await RootAdminAsync("IP4B");
        var slug = NewSlug();
        await CreateAsync(a, Azure(slug));

        var response = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, b, Azure(slug));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ProblemCodes.provider_slug_taken, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task School_admin_is_OutOfScope_and_manager_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "IP5");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var school = await SendAsync(factory, HttpMethod.Get, Routes.IdentityProviders, schoolAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, school.StatusCode);
        Assert.Contains("OutOfScope", await school.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, schoolAdmin, Azure(NewSlug()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.IdentityProviders, manager)).StatusCode);
    }

    [Fact]
    public async Task Client_B_providers_are_invisible()
    {
        var a = await RootAdminAsync("IP6A");
        var b = await RootAdminAsync("IP6B");
        var created = await CreateAsync(a, Azure(NewSlug()));

        var list = (await (await SendAsync(factory, HttpMethod.Get, Routes.IdentityProviders, b))
            .Content.ReadFromJsonAsync<List<IdentityProviderDto>>(Web))!;
        Assert.DoesNotContain(list, p => p.Id == created.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(factory, HttpMethod.Put, Routes.IdentityProvider(created.Id), b, Azure(created.Slug))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.IdentityProvider(created.Id), b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviderTest(created.Id), b)).StatusCode);
    }

    [Fact]
    public async Task Saving_registers_scheme_and_deleting_removes_it()
    {
        var admin = await RootAdminAsync("IP7");
        var created = await CreateAsync(admin, Azure(NewSlug()));
        var schemes = factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var name = ExternalSchemeRegistry.SchemeName(created.Id);

        Assert.NotNull(await schemes.GetSchemeAsync(name));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.IdentityProvider(created.Id), admin)).StatusCode);

        Assert.Null(await schemes.GetSchemeAsync(name));
    }

    [Fact]
    public async Task Test_endpoint_reports_unreachable_authority_without_500()
    {
        var admin = await RootAdminAsync("IP8");
        var created = await CreateAsync(admin, Oidc(NewSlug()));

        var response = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviderTest(created.Id), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProviderTestResultDto>(Web))!;
        Assert.False(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task Provider_change_is_audited_without_secret()
    {
        var admin = await RootAdminAsync("IP9");
        var created = await CreateAsync(admin, Azure(NewSlug()));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.IgnoreQueryFilters()
            .Where(e => e.EntityType == nameof(IdentityProviderConfig) && e.EntityId == created.Id).ToListAsync();
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.DoesNotContain(Secret, e.AfterJson ?? string.Empty);
            Assert.DoesNotContain("ProtectedClientSecret", e.AfterJson ?? string.Empty);
        });
    }

    [Fact]
    public async Task Changing_only_the_secret_is_audited_without_revealing_it()
    {
        var admin = await RootAdminAsync("IP13");
        var created = await CreateAsync(admin, Azure(NewSlug()));

        var put = await SendAsync(factory, HttpMethod.Put, Routes.IdentityProvider(created.Id), admin,
            Azure(created.Slug, secret: "a-brand-new-secret"));

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var updates = await db.AuditEntries.IgnoreQueryFilters()
            .Where(e => e.EntityType == nameof(IdentityProviderConfig) && e.EntityId == created.Id
                        && e.Action == Lms.Domain.Auditing.AuditAction.Updated).ToListAsync();
        var entry = Assert.Single(updates);
        Assert.Contains("ClientSecretUpdatedAt", entry.AfterJson);
        Assert.DoesNotContain("a-brand-new-secret", entry.AfterJson);
        Assert.DoesNotContain("ProtectedClientSecret", entry.AfterJson);
    }

    [Fact]
    public async Task Update_without_a_new_secret_does_not_touch_the_secret_timestamp()
    {
        var admin = await RootAdminAsync("IP14");
        var created = await CreateAsync(admin, Azure(NewSlug()));
        await using var before = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var stamp = (await before.Set<IdentityProviderConfig>().IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id)).ClientSecretUpdatedAt;
        Assert.NotNull(stamp);

        await SendAsync(factory, HttpMethod.Put, Routes.IdentityProvider(created.Id), admin,
            Azure(created.Slug, secret: null) with { DisplayNameEn = "Other" });

        await using var after = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(stamp, (await after.Set<IdentityProviderConfig>().IgnoreQueryFilters().SingleAsync(p => p.Id == created.Id)).ClientSecretUpdatedAt);
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("Has_Underscore")]
    [InlineData("UPPER")]
    public async Task Invalid_slug_is_rejected(string slug)
    {
        var admin = await RootAdminAsync("IP10");

        var response = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin, Azure(slug));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scopes_without_openid_and_national_id_without_claim_are_rejected()
    {
        var admin = await RootAdminAsync("IP11");

        var noOpenId = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin,
            Azure(NewSlug()) with { Scopes = "profile email" });
        var noClaim = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin,
            Azure(NewSlug()) with { MatchBy = ExternalMatchBy.NationalId, NationalIdClaim = null });

        Assert.Equal(HttpStatusCode.BadRequest, noOpenId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noClaim.StatusCode);
    }

    [Fact]
    public async Task New_provider_without_secret_is_rejected()
    {
        var admin = await RootAdminAsync("IP12");

        var response = await SendAsync(factory, HttpMethod.Post, Routes.IdentityProviders, admin, Azure(NewSlug(), secret: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
