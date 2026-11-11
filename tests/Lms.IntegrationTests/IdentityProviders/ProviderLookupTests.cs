using Lms.Domain.IdentityProviders;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Identity.External;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class ProviderLookupTests(LmsApiFactory factory)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private async Task<IdentityProviderConfig> AddAsync(Guid organizationId, string domains, bool enabled = true, bool deleted = false)
    {
        var provider = new IdentityProviderConfig(
            organizationId, IdentityProviderKind.Oidc, Unique("lk-"), "مزود", "Provider", "https://idp.example.test", null, "cid", null,
            "openid email", ExternalMatchBy.Email, null, domains, enabled);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Set<IdentityProviderConfig>().Add(provider);
        await db.SaveChangesAsync();
        if (deleted)
        {
            db.Remove(provider);
            await db.SaveChangesAsync();
        }

        return provider;
    }

    private async Task<T> WithLookupAsync<T>(Func<ProviderLookup, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProviderLookup>());
    }

    [Fact]
    public async Task Email_domain_finds_the_enabled_provider_case_insensitively()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PL1");
        var domain = Unique("d") + ".example";
        var provider = await AddAsync(tree.Org.Id, "other.example," + domain);

        var found = await WithLookupAsync(l => l.FindByEmailDomainAsync("Someone@" + domain.ToUpperInvariant(), CancellationToken.None));

        Assert.Equal(provider.Id, found!.Id);
    }

    [Fact]
    public async Task Disabled_deleted_and_suspended_client_providers_are_not_found()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PL2");
        var domain = Unique("e") + ".example";
        await AddAsync(tree.Org.Id, domain, enabled: false);
        await AddAsync(tree.Org.Id, domain, deleted: true);
        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("x@" + domain, CancellationToken.None)));

        var suspendedDomain = Unique("f") + ".example";
        var live = await AddAsync(tree.Org.Id, suspendedDomain);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Organizations.SingleAsync(o => o.Id == tree.Org.Id)).SetStatus(OrganizationStatus.Suspended);
            await db.SaveChangesAsync();
        }

        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("x@" + suspendedDomain, CancellationToken.None)));
        Assert.Null(await WithLookupAsync(l => l.FindBySlugAsync(live.Slug, CancellationToken.None)));
    }

    [Fact]
    public async Task Partial_domains_and_garbage_input_do_not_match()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PL3");
        var domain = Unique("g") + ".example";
        await AddAsync(tree.Org.Id, domain);

        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("x@sub." + domain, CancellationToken.None)));
        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("x@" + domain.Substring(1), CancellationToken.None)));
        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("not-an-email", CancellationToken.None)));
        Assert.Null(await WithLookupAsync(l => l.FindByEmailDomainAsync("", CancellationToken.None)));
    }

    [Fact]
    public async Task Slug_finds_the_enabled_provider()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PL4");
        var provider = await AddAsync(tree.Org.Id, Unique("h") + ".example");

        Assert.Equal(provider.Id, (await WithLookupAsync(l => l.FindBySlugAsync(provider.Slug, CancellationToken.None)))!.Id);
        Assert.Null(await WithLookupAsync(l => l.FindBySlugAsync("nope-nope", CancellationToken.None)));
    }
}
