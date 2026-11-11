using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class KeyProtectionStartupTests(LmsApiFactory factory)
{
    [Fact]
    public void Production_without_a_key_protection_certificate_fails_to_start_with_a_clear_message()
    {
        using var production = factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var ex = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        var message = string.Join(" | ", Chain(ex).Select(e => e.Message));
        Assert.Contains("DataProtection:Certificate", message);
        Assert.Contains("docs/runbook-key-protection.md", message);
    }

    private static IEnumerable<Exception> Chain(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            yield return ex;
        }
    }
}
