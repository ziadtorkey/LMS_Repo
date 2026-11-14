using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Staff;

[Collection(ApiCollection.Name)]
public sealed class CreateStaffTests : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public void Dispose() => _host.Dispose();
    private readonly FakeEmailSender _emails = new();

    public CreateStaffTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin)> AdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        return (tree, await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _host.CreateClient().SendAsync(request);
    }

    private static string Unique(string prefix = "t") => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static CreateStaffRequest Request(
        AccessTree tree, string? email = null, Guid? unit = null, string? roleKey = BuiltInRoles.Teacher,
        string? mobile = null, string? nationalId = null, DateOnly? expires = null, Guid? roleUnit = null) =>
        new("معلم", "Teacher", email ?? $"{Unique()}@lms.test", mobile, nationalId, "مدرس", "Teacher", null,
            unit ?? tree.School1.Id, expires,
            roleKey is null ? [] : [new StaffRoleInput(tree.Roles[roleKey].Id, roleUnit ?? unit ?? tree.School1.Id, null, null)]);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<StaffDetailDto> CreateOkAsync(RoleAssignment caller, CreateStaffRequest request)
    {
        var response = await SendAsync(HttpMethod.Post, Routes.Staff, caller, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StaffDetailDto>())!;
    }

    [Fact]
    public async Task Admin_creates_teacher_with_role_at_section_and_activation_email_is_sent()
    {
        var (tree, admin) = await AdminAsync("ST1");
        var request = Request(tree, email: $"  Teacher-{Guid.NewGuid():N}@LMS.test ", mobile: "+20 100-123 4567", nationalId: "ab12345");

        var dto = await CreateOkAsync(admin, request);

        var email = request.Email.Trim().ToLowerInvariant();
        Assert.Equal(email, dto.Summary.Email);
        Assert.Equal("+201001234567", dto.Summary.Mobile);
        Assert.Equal("AB12345", dto.NationalId);
        Assert.Equal(PersonStatuses.Active, dto.Summary.Status);
        Assert.False(dto.IsActivated);
        Assert.False(dto.IsLimitedView);
        var role = Assert.Single(dto.Summary.Roles);
        Assert.Equal(tree.School1.Id, role.OrgUnitId);
        Assert.False(role.IsShare);
        Assert.Contains("invite=1", Assert.Single(_emails.To(email)).HtmlBody);

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var profile = await db.Set<StaffProfile>().SingleAsync(p => p.Id == dto.Summary.Id);
        Assert.Equal(tree.School1.Path, profile.OrgUnitPath);
        Assert.Equal(email, (await db.Users.SingleAsync(u => u.Id == profile.UserId)).UserName);
        Assert.Single(await db.RoleAssignments.Where(a => a.UserId == profile.UserId).ToListAsync());
    }

    [Fact]
    public async Task Existing_user_from_another_client_is_reused_without_invitation_or_data_leak()
    {
        var (treeA, adminA) = await AdminAsync("ST2A");
        var (treeB, adminB) = await AdminAsync("ST2B");
        var email = $"{Unique("shared")}@lms.test";
        var first = await CreateOkAsync(adminA, Request(treeA, email: email));
        var sentBefore = _emails.To(email).Count;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // The person activated their account in client A: a password exists, so client B sends no invitation.
            var user = await db.Users.SingleAsync(u => u.Id == first.Summary.UserId);
            user.PasswordHash = "AQAAAAEAACcQAAAAEPlaceholderHashForTests==";
            await db.SaveChangesAsync();
        }

        var second = await CreateOkAsync(adminB, Request(treeB, email: email));

        Assert.Equal(first.Summary.UserId, second.Summary.UserId);
        Assert.Equal(sentBefore, _emails.To(email).Count);
        Assert.True(second.IsActivated);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("mobile")]
    [InlineData("nationalId")]
    public async Task Duplicate_in_sibling_school_is_409_with_field_only(string field)
    {
        var (tree, admin) = await AdminAsync("ST3" + field[..2]);
        var email = $"{Unique()}@lms.test";
        var mobile = "+2010" + Random.Shared.Next(10000000, 99999999);
        var nationalId = Unique("N").ToUpperInvariant();
        var first = await CreateOkAsync(admin, Request(tree, email: email, mobile: mobile, nationalId: nationalId, unit: tree.School1.Id)
            with { NameAr = "اسم-فريد-" + field, NameEn = "Unique-Name-" + field });

        var duplicate = Request(
            tree, unit: tree.School2.Id,
            email: field == "email" ? email : $"{Unique()}@lms.test",
            mobile: field == "mobile" ? mobile : "+2011" + Random.Shared.Next(10000000, 99999999),
            nationalId: field == "nationalId" ? nationalId : Unique("M").ToUpperInvariant());
        var response = await SendAsync(HttpMethod.Post, Routes.Staff, admin, duplicate);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Only the standard problem fields plus code and field: nothing that identifies the other person.
        var allowed = new HashSet<string> { "type", "title", "status", "detail", "instance", "traceId", "code", "field" };
        Assert.All(body.EnumerateObject(), p => Assert.Contains(p.Name, allowed));
        Assert.Equal(ProblemCodes.duplicate_person, body.GetProperty("code").GetString());
        Assert.Equal(field, body.GetProperty("field").GetString());
        var raw = body.GetRawText();
        foreach (var secret in new[] { first.Summary.Id.ToString(), first.Summary.UserId.ToString(), "Unique-Name", "اسم-فريد", "School", "مدرسة", tree.School1.Id.ToString() })
        {
            Assert.DoesNotContain(secret, raw);
        }
    }

    [Fact]
    public async Task Manager_can_create_teacher_but_not_admin_permission_escalation()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "ST4");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        await CreateOkAsync(manager, Request(tree));
        var response = await SendAsync(HttpMethod.Post, Routes.Staff, manager, Request(tree, roleKey: BuiltInRoles.OrgAdmin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemCodes.permission_escalation, await CodeAsync(response));
    }

    [Fact]
    public async Task Student_kind_role_is_400_invalid_role_kind()
    {
        var (tree, admin) = await AdminAsync("ST5");

        var response = await SendAsync(HttpMethod.Post, Routes.Staff, admin, Request(tree, roleKey: BuiltInRoles.Student));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_role_kind, await CodeAsync(response));
    }

    [Fact]
    public async Task Home_unit_in_other_client_is_404_and_missing_roles_or_past_expiry_are_400()
    {
        var (tree, admin) = await AdminAsync("ST6");
        var other = await AccessTestData.SeedAsync(_factory, "ST6other");

        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Post, Routes.Staff, admin, Request(tree, unit: other.School1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, Routes.Staff, admin, Request(tree, roleKey: null))).StatusCode);
        var expired = await SendAsync(HttpMethod.Post, Routes.Staff, admin,
            Request(tree, expires: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(ProblemCodes.invalid_expiry, await CodeAsync(expired));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, Routes.Staff, admin, Request(tree, mobile: "12345"))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_view_but_not_create_403()
    {
        var tree = await AccessTestData.SeedAsync(_factory, "ST7");
        var supervisor = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Supervisor, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, Routes.Staff, supervisor, Request(tree))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, Routes.Staff, supervisor)).StatusCode);
    }

    [Fact]
    public async Task Staff_create_is_audited()
    {
        var (tree, admin) = await AdminAsync("ST8");

        var dto = await CreateOkAsync(admin, Request(tree));

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(e => e.EntityId == dto.Summary.Id && e.Action == AuditAction.Created);
        Assert.Equal(nameof(StaffProfile), entry.EntityType);
        Assert.Equal(admin.UserId, entry.UserId);
        Assert.Equal(tree.School1.Path, entry.OrgUnitPath);
    }
}
