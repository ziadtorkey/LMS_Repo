using Lms.Domain.IdentityProviders;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class IdentityProviderIsolationTests(LmsApiFactory factory)
{
    private static IdentityProviderConfig Provider(Guid organizationId, string slug) => new(
        organizationId, IdentityProviderKind.Oidc, slug, "مزود", "Provider", "https://idp.example.test",
        azureTenantId: null, "client-id", "protected-secret", "openid profile email", ExternalMatchBy.Email,
        nationalIdClaim: null, emailDomains: "school.example", enabled: true);

    [Fact]
    public async Task Provider_of_client_B_is_invisible_to_client_A()
    {
        var a = await AccessTestData.SeedAsync(factory, "IPI1A");
        var b = await AccessTestData.SeedAsync(factory, "IPI1B");
        var slug = "isolation-" + Guid.NewGuid().ToString("N")[..8];
        Guid id;
        await using (var seed = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = Provider(b.Org.Id, slug);
            id = row.Id;
            seed.Set<IdentityProviderConfig>().Add(row);
            await seed.SaveChangesAsync();
        }

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        Assert.False(await asA.Set<IdentityProviderConfig>().AnyAsync(p => p.Id == id));
        await using var asB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.True(await asB.Set<IdentityProviderConfig>().AnyAsync(p => p.Id == id));
    }

    [Fact]
    public async Task Slug_is_unique_across_clients()
    {
        var a = await AccessTestData.SeedAsync(factory, "IPI2A");
        var b = await AccessTestData.SeedAsync(factory, "IPI2B");
        var slug = "unique-" + Guid.NewGuid().ToString("N")[..8];
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Set<IdentityProviderConfig>().Add(Provider(a.Org.Id, slug));
        await db.SaveChangesAsync();

        await using var second = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        second.Set<IdentityProviderConfig>().Add(Provider(b.Org.Id, slug));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
