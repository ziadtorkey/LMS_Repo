using Lms.Api.Infrastructure;
using Lms.Domain.Identity;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.People.Staff;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Staff;

[Collection(ApiCollection.Name)]
public sealed class StaffExpiryJobTests(LmsApiFactory factory)
{
    private static readonly DateOnly Day = new(2040, 6, 15);

    /// <summary>Runs the job the way the scheduler does: in its own scope with the platform (system) context.</summary>
    private static async Task RunAsync(IServiceProvider services, DateOnly asOf)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        await scope.ServiceProvider.GetRequiredService<StaffExpiryJob>().RunAsync(asOf, default);
    }

    private Task RunAsync(DateOnly asOf) => RunAsync(factory.Services, asOf);

    private async Task<(AccessTree Tree, Guid Admin, Guid Manager, Guid SiblingManager)> SeedAdminsAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, admin);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, manager);
        await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Manager, sibling);
        return (tree, admin, manager, sibling);
    }

    private async Task<StaffProfile> ReloadAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<StaffProfile>().AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<List<Notification>> NotificationsAsync(Guid user, string eventKey)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == user && n.EventKey == eventKey).ToListAsync();
    }

    [Fact]
    public void Daily_recurring_job_is_registered_at_startup_at_02_00_UTC()
    {
        using var scope = factory.Services.CreateScope();
        using var connection = scope.ServiceProvider.GetRequiredService<Hangfire.JobStorage>().GetConnection();

        var job = Assert.Single(connection.GetRecurringJobs(), r => r.Id == StaffExpiryJob.RecurringJobId);

        Assert.Equal("0 2 * * *", job.Cron);
    }

    [Fact]
    public async Task Staff_expired_yesterday_becomes_Expired_and_admins_above_are_notified()
    {
        var (tree, admin, manager, sibling) = await SeedAdminsAsync("SE1");
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(-1));

        await RunAsync(Day);

        Assert.Equal(PersonStatus.Expired, (await ReloadAsync(staff.Id)).Status);
        Assert.Single(await NotificationsAsync(admin, NotificationEvents.StaffExpired));
        Assert.Single(await NotificationsAsync(manager, NotificationEvents.StaffExpired));
        Assert.Empty(await NotificationsAsync(sibling, NotificationEvents.StaffExpired)); // the sibling school is not above
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var change = await db.Set<PersonStatusChange>().SingleAsync(c => c.ProfileId == staff.Id);
        Assert.Equal((PersonStatus.Active, PersonStatus.Expired, StaffExpiryJob.ExpiredReason), (change.From, change.To, change.Reason));
        Assert.Equal(UserStatus.Expired, (await db.Users.SingleAsync(u => u.Id == staff.UserId)).Status);
    }

    [Fact]
    public async Task Staff_on_the_last_day_is_not_expired_yet()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE2");
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day);

        await RunAsync(Day);

        Assert.Equal(PersonStatus.Active, (await ReloadAsync(staff.Id)).Status);
        Assert.Empty(await NotificationsAsync(admin, NotificationEvents.StaffExpired));
    }

    [Fact]
    public async Task Notice_is_sent_once_14_days_before_by_default()
    {
        var (tree, admin, manager, sibling) = await SeedAdminsAsync("SE3");
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(14));
        var later = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(16));

        await RunAsync(Day);
        await RunAsync(Day.AddDays(1));

        Assert.Single(await NotificationsAsync(admin, NotificationEvents.StaffExpiringSoon)); // the 14-day one only, once
        Assert.Single(await NotificationsAsync(manager, NotificationEvents.StaffExpiringSoon));
        Assert.Empty(await NotificationsAsync(sibling, NotificationEvents.StaffExpiringSoon));
        Assert.Equal(Day, (await ReloadAsync(staff.Id)).ExpiryNoticeSentOn);
        Assert.Null((await ReloadAsync(later.Id)).ExpiryNoticeSentOn);
    }

    [Fact]
    public async Task Notice_days_setting_at_school_overrides_root()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE4");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Lms.Domain.Configuration.OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, SettingKeys.StaffExpiryNoticeDays.Name, "3", false));
            db.Add(new Lms.Domain.Configuration.OrgUnitSetting(tree.Org.Id, tree.School1.Id, tree.School1.Path, SettingKeys.StaffExpiryNoticeDays.Name, "30", false));
            await db.SaveChangesAsync();
        }

        var inSchool1 = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(20));
        var inSchool2 = await PeopleTestKit.AddStaffAsync(factory, tree.School2, expiresOn: Day.AddDays(20));

        await RunAsync(Day);

        Assert.Equal(Day, (await ReloadAsync(inSchool1.Id)).ExpiryNoticeSentOn); // 30 days at School1
        Assert.Null((await ReloadAsync(inSchool2.Id)).ExpiryNoticeSentOn); // 3 days inherited from the root
        Assert.Single(await NotificationsAsync(admin, NotificationEvents.StaffExpiringSoon));
    }

    [Fact]
    public async Task Zero_notice_days_sends_no_notice()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Lms.Domain.Configuration.OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, SettingKeys.StaffExpiryNoticeDays.Name, "0", false));
            await db.SaveChangesAsync();
        }

        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(1));

        await RunAsync(Day);

        Assert.Empty(await NotificationsAsync(admin, NotificationEvents.StaffExpiringSoon));
        Assert.Null((await ReloadAsync(staff.Id)).ExpiryNoticeSentOn);
    }

    [Fact]
    public async Task Running_twice_on_the_same_day_changes_nothing()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE6");
        await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(-2));
        await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(5));

        await RunAsync(Day);
        await RunAsync(Day);

        Assert.Single(await NotificationsAsync(admin, NotificationEvents.StaffExpired));
        Assert.Single(await NotificationsAsync(admin, NotificationEvents.StaffExpiringSoon));
    }

    [Fact]
    public async Task Extending_ExpiresOn_resets_notice()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE7");
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(2));
        await RunAsync(Day);
        Assert.Equal(Day, (await ReloadAsync(staff.Id)).ExpiryNoticeSentOn);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<StaffProfile>().SingleAsync(p => p.Id == staff.Id);
            tracked.Update(tracked.NameAr, tracked.NameEn, tracked.Mobile, tracked.NationalId, null, null, null, Day.AddDays(10));
            await db.SaveChangesAsync();
        }

        Assert.Null((await ReloadAsync(staff.Id)).ExpiryNoticeSentOn);
        await RunAsync(Day.AddDays(1));
        Assert.Equal(Day.AddDays(1), (await ReloadAsync(staff.Id)).ExpiryNoticeSentOn);
        Assert.Equal(2, (await NotificationsAsync(admin, NotificationEvents.StaffExpiringSoon)).Count);
    }

    [Fact]
    public async Task Deactivated_and_deleted_staff_are_ignored()
    {
        var (tree, admin, _, _) = await SeedAdminsAsync("SE8");
        var deactivated = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(-1));
        var deleted = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: Day.AddDays(-1));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StaffProfile>().SingleAsync(p => p.Id == deactivated.Id)).SetStatus(PersonStatus.Deactivated);
            db.Set<StaffProfile>().Remove(await db.Set<StaffProfile>().SingleAsync(p => p.Id == deleted.Id));
            await db.SaveChangesAsync();
        }

        await RunAsync(Day);

        Assert.Equal(PersonStatus.Deactivated, (await ReloadAsync(deactivated.Id)).Status);
        Assert.Empty(await NotificationsAsync(admin, NotificationEvents.StaffExpired));
    }

    [Fact]
    public async Task Job_failure_in_client_A_still_processes_client_B()
    {
        var (a, _, _, _) = await SeedAdminsAsync("SE9A");
        var (b, adminB, _, _) = await SeedAdminsAsync("SE9B");
        var staffA = await PeopleTestKit.AddStaffAsync(factory, a.School1, expiresOn: Day.AddDays(-1));
        var staffB = await PeopleTestKit.AddStaffAsync(factory, b.School1, expiresOn: Day.AddDays(-1));
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Decorate(a.Org.Id)));

        await RunAsync(host.Services, Day);

        Assert.Equal(PersonStatus.Expired, (await ReloadAsync(staffB.Id)).Status);
        Assert.Single(await NotificationsAsync(adminB, NotificationEvents.StaffExpired));
        // Client A failed while notifying, after its status change was saved; nothing is left half-written for B.
        Assert.Equal(PersonStatus.Expired, (await ReloadAsync(staffA.Id)).Status);
    }
}

internal static class PublisherDecoration
{
    public static void Decorate(this IServiceCollection services, Guid failingOrganization)
    {
        var original = services.Last(d => d.ServiceType == typeof(INotificationPublisher));
        services.Remove(original);
        services.AddScoped<INotificationPublisher>(provider =>
        {
            var inner = original.ImplementationFactory is { } factory
                ? (INotificationPublisher)factory(provider)
                : (INotificationPublisher)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!);
            return new FailingPublisherProxy(inner, failingOrganization);
        });
    }

    private sealed class FailingPublisherProxy(INotificationPublisher inner, Guid failingOrganization) : INotificationPublisher
    {
        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default) =>
            request.OrganizationId == failingOrganization
                ? throw new InvalidOperationException("push failed")
                : inner.PublishAsync(request, cancellationToken);
    }
}
