using System.Net;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Seeding;

/// <summary>
/// Boots the real application in the Development environment (DI validation on, no test service overrides),
/// the way `dotnet run` does: catches missing registrations and a broken startup seed that unit-style tests miss.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DevelopmentStartupTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Api_boots_in_Development_with_valid_DI_and_seeds_the_sample_clients()
    {
        var connectionString = await factory.CreateIsolatedDatabaseAsync();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Lms", connectionString);
        });

        var health = await host.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await using var db = LmsDbContextFactory.Create(connectionString, TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Organizations.CountAsync());
    }
}