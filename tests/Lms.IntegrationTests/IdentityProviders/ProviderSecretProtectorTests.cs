using Lms.Infrastructure.Identity.External;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class ProviderSecretProtectorTests(LmsApiFactory factory)
{
    private static IDataProtectionProvider ProviderOver(IXmlRepository repository)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("Lms")
            .AddKeyManagementOptions(o => o.XmlRepository = repository);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private EfXmlRepository Repository() => new(new SingleContextScopeFactory(factory));

    [Fact]
    public void Protected_value_round_trips_and_is_not_the_plaintext()
    {
        var protector = new ProviderSecretProtector(ProviderOver(Repository()));

        var cipher = protector.Protect("s3cret-value");

        Assert.NotEqual("s3cret-value", cipher);
        Assert.DoesNotContain("s3cret", cipher);
        Assert.Equal("s3cret-value", protector.Unprotect(cipher));
    }

    [Fact]
    public void Unprotect_of_garbage_returns_null_instead_of_throwing()
    {
        var protector = new ProviderSecretProtector(ProviderOver(Repository()));

        Assert.Null(protector.Unprotect("not-a-protected-payload"));
    }

    [Fact]
    public void Another_purpose_cannot_read_the_secret()
    {
        var provider = ProviderOver(Repository());
        var cipher = new ProviderSecretProtector(provider).Protect("abc");

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => provider.CreateProtector("Some.Other.Purpose").Unprotect(cipher));
    }

    [Fact]
    public void Keys_are_persisted_in_the_database_so_a_second_provider_can_read_them()
    {
        var cipher = new ProviderSecretProtector(ProviderOver(Repository())).Protect("shared-across-nodes");

        // A brand new provider (as after a restart or on another node) over the same table.
        var second = new ProviderSecretProtector(ProviderOver(Repository()));

        Assert.Equal("shared-across-nodes", second.Unprotect(cipher));
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(db.Set<DataProtectionKey>().Any());
    }

    private sealed class SingleContextScopeFactory(LmsApiFactory factory) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(factory.CreateDbContext(TestCurrentContext.PlatformAdmin()));

        private sealed class Scope(LmsDbContext db) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) => serviceType == typeof(LmsDbContext) ? db : null;

            public void Dispose() => db.Dispose();
        }
    }
}
