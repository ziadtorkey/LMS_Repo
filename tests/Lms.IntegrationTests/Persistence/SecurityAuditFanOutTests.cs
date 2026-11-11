using Lms.Domain.Auditing;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Persistence;

/// <summary>D20: security events are written once per client in which the user holds a valid assignment.</summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityAuditFanOutTests(LmsApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task WriteAsync(string name, Guid? userId, string? details = null)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteSecurityEventAsync(name, userId, details, "203.0.113.5");
    }

    private async Task<List<AuditEntry>> AsAsync(Guid orgId, string rootPath, Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.For(orgId, rootPath));
        return await db.AuditEntries.Where(e => e.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task User_with_assignments_in_two_clients_gets_one_entry_per_client_each_visible_only_there()
    {
        var a = await AccessTestData.SeedAsync(factory, "SA1");
        var b = await AccessTestData.SeedAsync(factory, "SA2");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.Manager, user);
        // Two assignments in client A still give one entry for A.
        await AccessTestData.AssignAsync(factory, a, a.School2, BuiltInRoles.Teacher, user);

        await WriteAsync("LoginSucceeded", user);

        await using var all = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await all.AuditEntries.Where(e => e.UserId == user).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { a.Org.Id, b.Org.Id }.Order(), entries.Select(e => e.OrganizationId!.Value).Order());
        Assert.All(entries, e =>
        {
            Assert.Equal(AuditAction.SecurityEvent, e.Action);
            Assert.Equal("LoginSucceeded", e.EntityType);
            Assert.Equal("203.0.113.5", e.IpAddress);
        });

        Assert.Equal(a.Org.Id, Assert.Single(await AsAsync(a.Org.Id, a.Root.Path, user)).OrganizationId);
        Assert.Equal(b.Org.Id, Assert.Single(await AsAsync(b.Org.Id, b.Root.Path, user)).OrganizationId);
    }

    [Fact]
    public async Task Expired_and_future_assignments_do_not_count()
    {
        var a = await AccessTestData.SeedAsync(factory, "SA3");
        var b = await AccessTestData.SeedAsync(factory, "SA4");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.Teacher, user);
        await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.Teacher, user, validTo: Today.AddDays(-1));

        await WriteAsync("PasswordResetRequested", user);

        await using var all = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(a.Org.Id, Assert.Single(await all.AuditEntries.Where(e => e.UserId == user).ToListAsync()).OrganizationId);
    }

    [Fact]
    public async Task User_without_assignment_keeps_one_null_org_entry()
    {
        var user = Guid.NewGuid();

        await WriteAsync("LoginFailed", user, "{\"reason\":\"bad password\"}");

        await using var all = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = Assert.Single(await all.AuditEntries.Where(e => e.UserId == user).ToListAsync());
        Assert.Null(entry.OrganizationId);
    }

    [Fact]
    public async Task Unknown_email_failure_writes_one_null_org_entry()
    {
        var marker = $"{{\"email\":\"nobody-{Guid.NewGuid():N}@lms.test\"}}";

        await WriteAsync("LoginFailed", null, marker);

        await using var all = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = Assert.Single(await all.AuditEntries.Where(e => e.AfterJson == marker).ToListAsync());
        Assert.Null(entry.OrganizationId);
        Assert.Null(entry.UserId);
    }

    [Fact]
    public async Task Null_org_entries_are_invisible_to_client_contexts()
    {
        var a = await AccessTestData.SeedAsync(factory, "SA5");
        var user = Guid.NewGuid();
        await WriteAsync("LoginFailed", user);

        Assert.Empty(await AsAsync(a.Org.Id, a.Root.Path, user));
    }
}
