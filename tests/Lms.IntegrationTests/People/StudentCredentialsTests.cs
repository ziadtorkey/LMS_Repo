using System.Collections.Concurrent;
using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lms.IntegrationTests.People;

/// <summary>D24, D45: the student's account, default password, delivery after commit, and no clear password anywhere else.</summary>
[Collection(ApiCollection.Name)]
public sealed class StudentCredentialsTests
{
    private readonly LmsApiFactory _factory;
    private readonly FakeEmailSender _emails = new();
    private readonly CapturingLogs _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public StudentCredentialsTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IEmailSender>(_emails);
            s.AddSingleton<ILoggerProvider>(_logs);
        }));
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Sink(this);

        public void Dispose()
        {
        }

        private sealed class Sink(CapturingLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Lines.Enqueue(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
        }
    }

    private static string Mail(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16] + "@lms.test";

    private async Task<(StudentProfile Student, AccessTree Tree)> StudentAsync(string name, string? email = null)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var student = new StudentProfile(
            tree.School1, "S" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant(), "طالب", "Student " + name,
            new DateOnly(2014, 5, 1), "Male", "Muslim", "EG");
        student.Update("طالب", "Student " + name, null, null, new DateOnly(2014, 5, 1), "Male", "Muslim", "EG", null, null, email, null);
        db.Set<StudentProfile>().Add(student);
        await db.SaveChangesAsync();
        return (student, tree);
    }

    private async Task<IssuedCredentials> IssueAsync(Guid studentId, string? requested = null, bool reset = false, bool flush = true)
    {
        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.NewGuid(), isPlatformAdmin: true);
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
        var student = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == studentId);
        var credentials = scope.ServiceProvider.GetRequiredService<IStudentCredentials>();
        var issued = await credentials.IssueAsync(student, requested, reset, CancellationToken.None);
        await db.SaveChangesAsync();
        if (flush)
        {
            await credentials.FlushDeliveriesAsync(CancellationToken.None);
        }

        return issued;
    }

    [Fact]
    public async Task Issue_creates_the_account_with_a_generated_default_password_that_must_be_changed()
    {
        var (student, _) = await StudentAsync("SC1");

        var issued = await IssueAsync(student.Id);

        Assert.Equal($"{student.StudentCode.ToLowerInvariant()}@{await ShortCodeOf(student)}", issued.User.UserName);
        Assert.Equal(PasswordGeneratorLength, issued.Password!.Length);
        using var scope = _host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users.FindByIdAsync(issued.User.Id.ToString()))!;
        Assert.True(stored.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(stored, issued.Password));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(stored.Id, (await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == student.Id)).UserId);
    }

    private const int PasswordGeneratorLength = 12;

    private async Task<string> ShortCodeOf(StudentProfile student)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Organizations.Where(o => o.Id == student.OrganizationId).Select(o => o.ShortCode).SingleAsync();
    }

    [Fact]
    public async Task Requested_password_is_used_and_a_weak_one_is_refused_before_anything_is_created()
    {
        var (student, _) = await StudentAsync("SC2");
        var weak = await Assert.ThrowsAsync<StudentCredentialsException>(() => IssueAsync(student.Id, "short"));
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.Contains("PasswordTooShort", weak.Errors);
            Assert.False(await db.Users.AnyAsync(u => u.UserName!.StartsWith(student.StudentCode.ToLowerInvariant() + "@")));
        }

        var issued = await IssueAsync(student.Id, "Chosen1Pass");

        Assert.Equal("Chosen1Pass", issued.Password);
    }

    [Fact]
    public async Task Credentials_email_goes_to_student_else_primary_guardian_else_none()
    {
        var own = Mail("kid");
        var (withEmail, _) = await StudentAsync("SC3A", own);
        var toStudent = await IssueAsync(withEmail.Id);
        Assert.Equal(CredentialsDelivery.Student, toStudent.DeliveredTo);
        var mail = Assert.Single(_emails.To(own));
        Assert.Contains(toStudent.Password!, mail.HtmlBody);
        Assert.Contains(withEmail.StudentCode.ToLowerInvariant(), mail.HtmlBody);

        var (viaGuardian, tree) = await StudentAsync("SC3B");
        var guardianEmail = Mail("mum");
        await LinkGuardianAsync(viaGuardian, tree, guardianEmail);
        var toGuardian = await IssueAsync(viaGuardian.Id);
        Assert.Equal(CredentialsDelivery.PrimaryGuardian, toGuardian.DeliveredTo);
        Assert.Single(_emails.To(guardianEmail));

        var (nobody, _) = await StudentAsync("SC3C");
        var before = _emails.Sent.Count;
        var none = await IssueAsync(nobody.Id);
        Assert.Equal(CredentialsDelivery.None, none.DeliveredTo);
        Assert.NotNull(none.Password); // shown once to the school instead
        Assert.Equal(before, _emails.Sent.Count);
    }

    private async Task LinkGuardianAsync(StudentProfile student, AccessTree tree, string email)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
        var stored = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == student.Id);
        var admin = TestCurrentContext.PlatformAdmin();
        await using var adminDb = _factory.CreateDbContext(admin);
        var guardian = new GuardianProfile(tree.Org.Id, "ولي", "Parent", "+2010" + Random.Shared.Next(10000000, 99999999), email, null);
        adminDb.Set<GuardianProfile>().Add(guardian);
        adminDb.Set<StudentGuardian>().Add(StudentGuardian.For(stored, guardian, "Mother", true));
        await adminDb.SaveChangesAsync();
    }

    [Fact]
    public async Task Credentials_are_sent_only_after_the_flush()
    {
        var own = Mail("late");
        var (student, _) = await StudentAsync("SC4", own);

        var issued = await IssueAsync(student.Id, flush: false);

        Assert.Empty(_emails.To(own)); // queued, not sent: the caller flushes after its transaction committed
        Assert.NotNull(issued.Password);
    }

    [Fact]
    public async Task Password_never_appears_in_logs_or_audit()
    {
        var own = Mail("quiet");
        var (student, tree) = await StudentAsync("SC5", own);
        _emails.Fail = true; // the failing send is logged with its exception: the password must not be in it
        var issued = await IssueAsync(student.Id, "Secret9Phrase");
        _emails.Fail = false;

        Assert.Equal("Secret9Phrase", issued.Password);
        Assert.DoesNotContain(_logs.Lines, line => line.Contains("Secret9Phrase"));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.IgnoreQueryFilters().Where(e => e.EntityId == issued.User.Id || e.UserId == issued.User.Id).ToListAsync();
        Assert.DoesNotContain(entries, e => (e.AfterJson ?? string.Empty).Contains("Secret9Phrase") || (e.BeforeJson ?? string.Empty).Contains("Secret9Phrase"));
        Assert.DoesNotContain(entries, e => (e.AfterJson ?? string.Empty).Contains("PasswordHash"));
        Assert.NotNull(tree);
    }

    [Fact]
    public async Task An_existing_account_with_its_own_password_is_left_alone_unless_reset()
    {
        var shared = Mail("both");
        var (student, _) = await StudentAsync("SC6", shared);
        string originalHash;
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var staff = new User { UserName = shared, Email = shared, DisplayNameEn = "Staff and parent" };
            Assert.True((await users.CreateAsync(staff, "TheirOwn1Pass")).Succeeded);
            originalHash = staff.PasswordHash!;
            await using var seed = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var home = await seed.OrgUnits.SingleAsync(u => u.Id == student.OrgUnitId);
            seed.Set<StaffProfile>().Add(new StaffProfile(home, staff.Id, "موظف", "Staff", shared, null, null, null, null, null, null)); // a profile in this client
            await seed.SaveChangesAsync();
        }

        var kept = await IssueAsync(student.Id);

        Assert.Null(kept.Password);
        Assert.Equal(CredentialsDelivery.None, kept.DeliveredTo);
        using (var scope = _host.Services.CreateScope())
        {
            var stored = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(shared))!;
            Assert.Equal(originalHash, stored.PasswordHash);
            Assert.False(stored.MustChangePassword);
        }

        string stampBefore;
        using (var scope = _host.Services.CreateScope())
        {
            stampBefore = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(shared))!.SecurityStamp!;
        }

        var reset = await IssueAsync(student.Id, reset: true);

        Assert.NotNull(reset.Password);
        using (var scope = _host.Services.CreateScope())
        {
            var stamp = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(shared))!.SecurityStamp;
            Assert.NotEqual(stampBefore, stamp); // sessions that used the old password end
        }

        using var after = _host.Services.CreateScope();
        Assert.True((await after.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(shared))!.MustChangePassword);
    }

    [Fact]
    public async Task Reset_gives_a_new_password_and_forces_the_change_again()
    {
        var (student, _) = await StudentAsync("SC7");
        var first = await IssueAsync(student.Id);
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync(first.User.Id.ToString()))!;
            user.MustChangePassword = false; // the student changed it
            await users.UpdateAsync(user);
        }

        var second = await IssueAsync(student.Id, reset: true);

        Assert.NotEqual(first.Password, second.Password);
        using var after = _host.Services.CreateScope();
        var users2 = after.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users2.FindByIdAsync(first.User.Id.ToString()))!;
        Assert.True(stored.MustChangePassword);
        Assert.True(await users2.CheckPasswordAsync(stored, second.Password!));
        Assert.False(await users2.CheckPasswordAsync(stored, first.Password!));
    }

    [Fact]
    public async Task Same_student_code_in_two_clients_never_shares_an_account()
    {
        var (first, treeA) = await StudentAsync("SC8A");
        var (second, treeB) = await StudentAsync("SC8B");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == second.Id);
            stored.ChangeCode(first.StudentCode); // codes are unique per client, so another client may use the same one
            await db.SaveChangesAsync();
        }

        var a = await IssueAsync(first.Id);
        var b = await IssueAsync(second.Id);

        Assert.NotEqual(a.User.Id, b.User.Id); // the second student did not take over the first one's account
        Assert.NotNull(b.Password);
        var code = first.StudentCode.ToLowerInvariant();
        Assert.Equal($"{code}@{await ShortCodeOf(first)}", a.User.UserName); // D47: distinct logins, no suffix games
        Assert.Equal($"{code}@{await ShortCodeOf(second)}", b.User.UserName);
        Assert.NotEqual(a.User.UserName, b.User.UserName);
        using var scope = _host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(a.User.Id.ToString()))!, a.Password!)); // the first account is untouched
        Assert.NotNull(treeA);
        Assert.NotNull(treeB);
    }

    [Fact]
    public async Task An_email_that_belongs_to_an_account_of_another_client_is_refused_not_taken_over()
    {
        var shared = Mail("other");
        var (student, _) = await StudentAsync("SC9", shared);
        var (_, otherTree) = await StudentAsync("SC9B");
        string originalHash;
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var owner = new User { UserName = shared, Email = shared, DisplayNameEn = "Staff of another client" };
            Assert.True((await users.CreateAsync(owner, "TheirOwn1Pass")).Succeeded);
            originalHash = owner.PasswordHash!;
            await using var seed = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var home = await seed.OrgUnits.SingleAsync(u => u.Id == otherTree.School1.Id);
            seed.Set<StaffProfile>().Add(new StaffProfile(home, owner.Id, "موظف", "Staff", shared, null, null, null, null, null, null));
            await seed.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<DuplicateAccountException>(() => IssueAsync(student.Id));
        Assert.Equal("Email", ex.Field);
        await Assert.ThrowsAsync<DuplicateAccountException>(() => IssueAsync(student.Id, reset: true));

        using var after = _host.Services.CreateScope();
        var stored = (await after.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(shared))!;
        Assert.Equal(originalHash, stored.PasswordHash); // untouched
        Assert.False(stored.MustChangePassword);
    }

    [Fact]
    public async Task An_account_with_no_profile_at_all_is_not_reused_either()
    {
        var shared = Mail("orphan");
        var (student, _) = await StudentAsync("SC10", shared);
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(new User { UserName = shared, Email = shared, DisplayNameEn = "Someone" }, "TheirOwn1Pass")).Succeeded);
        }

        await Assert.ThrowsAsync<DuplicateAccountException>(() => IssueAsync(student.Id));
    }
}
