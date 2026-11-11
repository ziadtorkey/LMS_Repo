using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class PersonAccountsTests
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;
    private readonly FakeEmailSender _emails = new();

    public PersonAccountsTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

    [Fact]
    public async Task EnsureUser_reuses_existing_email_and_creates_without_password()
    {
        using var scope = _host.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IPersonAccounts>();
        var email = $"new-{Guid.NewGuid():N}@lms.test";

        var (created, wasCreated) = await accounts.EnsureUserAsync(email, email, "جديد", "New", default);
        var (again, createdAgain) = await accounts.EnsureUserAsync(email.ToUpperInvariant(), "other-name", "x", "x", default);

        Assert.True(wasCreated);
        Assert.False(createdAgain);
        Assert.Equal(created.Id, again.Id);
        Assert.Null(created.PasswordHash);
        Assert.Equal("New", created.DisplayNameEn);
        Assert.True(created.LockoutEnabled);
    }

    [Fact]
    public async Task EnsureUser_finds_a_user_without_email_by_user_name()
    {
        using var scope = _host.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IPersonAccounts>();
        var userName = "stu-" + Guid.NewGuid().ToString("N");

        var (first, created) = await accounts.EnsureUserAsync(null, userName, "طالب", "Student", default);
        var (second, createdAgain) = await accounts.EnsureUserAsync(null, userName, "طالب", "Student", default);

        Assert.True(created);
        Assert.False(createdAgain);
        Assert.Equal(first.Id, second.Id);
        Assert.Null(first.Email);
    }

    private async Task<User> NewUserAsync(bool withPassword = false)
    {
        using var scope = _host.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IPersonAccounts>();
        var email = $"inv-{Guid.NewGuid():N}@lms.test";
        var (user, _) = await accounts.EnsureUserAsync(email, email, "مدعو", "Invitee", default);
        if (withPassword)
        {
            await scope.ServiceProvider.GetRequiredService<UserManager<User>>().AddPasswordAsync(user, "Passw0rd!x");
        }

        return user;
    }

    [Fact]
    public async Task Invitation_is_sent_after_commit_and_not_after_rollback()
    {
        var committed = await NewUserAsync();
        var rolledBack = await NewUserAsync();

        using (var scope = _host.Services.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IInvitationOutbox>();
            outbox.Enqueue(committed.Id, "en");
            Assert.Empty(_emails.To(committed.Email!)); // nothing goes out before the flush
            await outbox.FlushAsync(default);
        }

        using (var scope = _host.Services.CreateScope())
        {
            // The handler failed before commit: it never flushes, so nothing is sent.
            scope.ServiceProvider.GetRequiredService<IInvitationOutbox>().Enqueue(rolledBack.Id, "en");
        }

        Assert.Single(_emails.To(committed.Email!));
        Assert.Empty(_emails.To(rolledBack.Email!));
    }

    [Fact]
    public async Task Invitation_to_alternate_email_reaches_that_address()
    {
        var user = await NewUserAsync();
        var alternate = $"alt-{Guid.NewGuid():N}@lms.test";
        using var scope = _host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IInvitationOutbox>();

        outbox.Enqueue(user.Id, "ar", alternate);
        await outbox.FlushAsync(default);

        Assert.Single(_emails.To(alternate));
        Assert.Empty(_emails.To(user.Email!));
    }

    [Fact]
    public async Task Invitation_is_skipped_for_users_that_already_have_a_password_and_failures_do_not_throw()
    {
        var active = await NewUserAsync(withPassword: true);
        var pending = await NewUserAsync();
        using var scope = _host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IInvitationOutbox>();

        outbox.Enqueue(active.Id, "en");
        await outbox.FlushAsync(default);
        Assert.Empty(_emails.To(active.Email!));

        _emails.Fail = true;
        outbox.Enqueue(pending.Id, "en");
        await outbox.FlushAsync(default); // logged, not thrown
        _emails.Fail = false;
        Assert.Empty(_emails.To(pending.Email!));
    }

    [Fact]
    public async Task Flushing_twice_does_not_send_twice()
    {
        var user = await NewUserAsync();
        using var scope = _host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IInvitationOutbox>();
        outbox.Enqueue(user.Id, "en");

        await outbox.FlushAsync(default);
        await outbox.FlushAsync(default);

        Assert.Single(_emails.To(user.Email!));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.Users.AnyAsync(u => u.Id == user.Id));
    }
}
