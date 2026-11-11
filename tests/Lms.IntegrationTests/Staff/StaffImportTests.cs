using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Imports;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Staff;

/// <summary>M2 Task 17 (#165, FR-STF-04, X-06): staff Excel import; all-or-nothing, one commit, policies apply to every row.</summary>
[Collection(ApiCollection.Name)]
public sealed class StaffImportTests : IDisposable
{
    private const string Kind = "staff";
    private static readonly string[] EnHeaders =
        ["Name (Arabic)", "Name (English)", "Email", "Mobile", "National ID", "Job title (Arabic)", "Job title (English)", "Role", "Unit code", "Role valid from", "Account expires on"];
    private static readonly string[] ArHeaders =
        ["الاسم بالعربية", "الاسم بالإنجليزية", "البريد الإلكتروني", "رقم الجوال", "رقم الهوية الوطنية", "المسمى الوظيفي بالعربية", "المسمى الوظيفي بالإنجليزية", "الدور", "رمز الوحدة", "بداية صلاحية الدور", "تاريخ انتهاء الحساب"];

    private readonly LmsApiFactory _factory;
    private readonly FakeEmailSender _emails = new();
    private readonly ImportTestHost _host;

    public StaffImportTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = new ImportTestHost(factory, withTestDefinition: false, services =>
        {
            services.AddSingleton<IEmailSender>(_emails);
            services.RemoveAll<IPolicyEvaluator>(); // the real evaluator, so role-blocking policies apply
            services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        });
    }

    public void Dispose() => _host.Dispose();

    private static string Unique(string prefix = "s") => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        return (tree, await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    private static string?[] Row(string email, string unitCode, string role = "Teacher", string? mobile = null, string? nationalId = null,
        string? expires = null) =>
        ["معلم", "Teacher " + email[..6], email, mobile, nationalId, "مدرس", "Teacher", role, unitCode, null, expires];

    private async Task<Guid> UploadAsync(RoleAssignment admin, Guid unitId, string[] headers, params string?[][] rows)
    {
        var response = await _host.UploadAsync(admin, unitId, XlsxBuilder.Create(headers, rows), "staff.xlsx", kind: Kind);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await _host.RunQueuedJobsAsync();
        return id;
    }

    private async Task<List<(int Row, string? Column, string Code)>> ErrorsAsync(RoleAssignment admin, Guid id)
    {
        var rows = await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, id) + "?errorsOnly=true&pageSize=200", admin);
        var items = JsonDocument.Parse(await rows.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        return items.EnumerateArray().SelectMany(r => r.GetProperty("errors").EnumerateArray().Select(e => (
            r.GetProperty("rowNumber").GetInt32(),
            e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
            e.GetProperty("code").GetString()!))).ToList();
    }

    private async Task CommitAsync(RoleAssignment admin, Guid id)
    {
        var commit = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        Assert.Equal(HttpStatusCode.Accepted, commit.StatusCode);
        await _host.RunQueuedJobsAsync();
    }

    private async Task<int> StaffCountAsync(Guid organizationId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<StaffProfile>().IgnoreQueryFilters().CountAsync(p => p.OrganizationId == organizationId);
    }

    [Fact]
    public async Task Valid_file_of_50_staff_imports_all_and_sends_50_invitations_after_commit()
    {
        var (tree, admin) = await SetUpAsync("SI1");
        var emails = Enumerable.Range(0, 50).Select(_ => $"{Unique("imp")}@lms.test").ToList();
        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders, emails.Select(e => Row(e, "S1")).ToArray());
        var state = await _host.BatchAsync(_factory, id);
        Assert.Equal("Valid", state.Status);
        Assert.Equal(50, state.RowCount);
        Assert.All(emails, e => Assert.Empty(_emails.To(e))); // nothing is sent while only previewed

        await CommitAsync(admin, id);

        state = await _host.BatchAsync(_factory, id);
        Assert.Equal("Committed", state.Status);
        Assert.Equal(50, state.CreatedCount);
        Assert.Equal(50, await StaffCountAsync(tree.Org.Id));
        Assert.All(emails, e => Assert.Single(_emails.To(e)));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(50, await db.RoleAssignments.CountAsync(a => a.OrgUnitId == tree.School1.Id && a.RoleId == tree.Roles[BuiltInRoles.Teacher].Id));
    }

    [Fact]
    public async Task Arabic_headers_file_is_accepted()
    {
        var (tree, admin) = await SetUpAsync("SI2");
        var id = await UploadAsync(admin, tree.Root.Id, ArHeaders, Row($"{Unique()}@lms.test", "S1", role: "المعلم"));

        Assert.Equal("Valid", (await _host.BatchAsync(_factory, id)).Status);
    }

    [Fact]
    public async Task Row_with_existing_email_in_sibling_school_blocks_whole_file_with_error_report()
    {
        var (tree, admin) = await SetUpAsync("SI3");
        var existing = $"{Unique("old")}@lms.test";
        var first = await UploadAsync(admin, tree.Root.Id, EnHeaders, Row(existing, "S2"));
        await CommitAsync(admin, first);
        Assert.Equal(1, await StaffCountAsync(tree.Org.Id));

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders, Row($"{Unique()}@lms.test", "S1"), Row(existing.ToUpperInvariant(), "S1"));

        var state = await _host.BatchAsync(_factory, id);
        Assert.Equal("Invalid", state.Status);
        Assert.NotNull(state.ErrorReportFileId);
        Assert.Contains((3, "Email", "duplicate_person"), await ErrorsAsync(admin, id));
        Assert.Equal(HttpStatusCode.Conflict, (await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin)).StatusCode);
        Assert.Equal(1, await StaffCountAsync(tree.Org.Id)); // the valid row 2 was not imported either
    }

    [Fact]
    public async Task Duplicate_email_inside_file_points_to_other_row()
    {
        var (tree, admin) = await SetUpAsync("SI4");
        var email = $"{Unique()}@lms.test";

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders,
            Row(email, "S1", mobile: "+201001110000", nationalId: "AB11111"), Row($"{Unique()}@lms.test", "S1"),
            Row(email.ToUpperInvariant(), "S1", mobile: "+201001110000", nationalId: "ab11111"));

        Assert.Equal("Invalid", (await _host.BatchAsync(_factory, id)).Status);
        var errors = await ErrorsAsync(admin, id);
        Assert.Contains((4, "Email", "duplicate_in_file"), errors);
        Assert.Contains((4, "Mobile", "duplicate_in_file"), errors);
        Assert.Contains((4, "NationalId", "duplicate_in_file"), errors);
        Assert.DoesNotContain(errors, e => e.Row == 3);
    }

    [Fact]
    public async Task Unknown_unit_code_or_role_is_reported()
    {
        var (tree, admin) = await SetUpAsync("SI5");

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders,
            Row($"{Unique()}@lms.test", "NOPE"), Row($"{Unique()}@lms.test", "S1", role: "Astronaut"),
            Row($"{Unique()}@lms.test", "S1", role: "Student"), Row("not-an-email", "S1", mobile: "12"));

        var errors = await ErrorsAsync(admin, id);
        Assert.Contains((2, "UnitCode", "unknown_unit"), errors);
        Assert.Contains((3, "Role", "unknown_role"), errors);
        Assert.Contains((4, "Role", "invalid_role_kind"), errors);
        Assert.Contains((5, "Email", "invalid_email"), errors);
        Assert.Contains((5, "Mobile", "invalid_mobile"), errors);
    }

    [Fact]
    public async Task Past_expiry_and_bad_dates_are_reported()
    {
        var (tree, admin) = await SetUpAsync("SI6");

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders,
            Row($"{Unique()}@lms.test", "S1", expires: "2001-01-01"), Row($"{Unique()}@lms.test", "S1", expires: "soon"));

        var errors = await ErrorsAsync(admin, id);
        Assert.Contains((2, "ExpiresOn", "invalid_expiry"), errors);
        Assert.Contains((3, "ExpiresOn", "invalid_date"), errors);
    }

    [Fact]
    public async Task Teacher_row_under_CreateTeachers_policy_is_blocked_policy()
    {
        var (tree, admin) = await SetUpAsync("SI7");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Lms.Domain.Configuration.OrgUnitPolicy(tree.Org.Id, tree.School1.Id, tree.School1.Path, PolicyCatalog.CreateTeachers));
            await db.SaveChangesAsync();
        }

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders,
            Row($"{Unique()}@lms.test", "S1"), Row($"{Unique()}@lms.test", "S1", role: "Counselor"), Row($"{Unique()}@lms.test", "S2"));

        var errors = await ErrorsAsync(admin, id);
        Assert.Equal([(2, "Role", "blocked_by_policy")], errors); // Counselor and the other school are fine
    }

    [Fact]
    public async Task CreateStaff_and_CreateAdmins_policies_block_import_rows()
    {
        var (tree, admin) = await SetUpAsync("SI8");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Lms.Domain.Configuration.OrgUnitPolicy(tree.Org.Id, tree.School1.Id, tree.School1.Path, PolicyCatalog.CreateStaff));
            db.Add(new Lms.Domain.Configuration.OrgUnitPolicy(tree.Org.Id, tree.School2.Id, tree.School2.Path, PolicyCatalog.CreateAdmins));
            await db.SaveChangesAsync();
        }

        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders,
            Row($"{Unique()}@lms.test", "S1", role: "Counselor"), Row($"{Unique()}@lms.test", "S2", role: "OrgAdmin"),
            Row($"{Unique()}@lms.test", "S2", role: "Counselor"));

        var errors = await ErrorsAsync(admin, id);
        Assert.Contains(errors, e => e.Row == 2 && e.Code == "blocked_by_policy"); // CreateStaff blocks Staff.Import at School1
        Assert.Contains(errors, e => e.Row == 3 && e.Code == "blocked_by_policy"); // CreateAdmins blocks OrgAdmin at School2
        Assert.DoesNotContain(errors, e => e.Row == 4);
    }

    [Fact]
    public async Task Role_escalation_is_reported_per_row()
    {
        var (tree, _) = await SetUpAsync("SI9");
        Role limited;
        RoleAssignment caller;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            limited = new Role(tree.Org.Id, null, "محدود", "Limited importer", RoleBaseKind.Staff);
            db.Add(limited);
            foreach (var permission in new[] { Permissions.Staff.Import, Permissions.Staff.View, Permissions.Assignments.Create })
            {
                db.Add(new RolePermission(tree.Org.Id, limited.Id, permission));
            }

            caller = new RoleAssignment(tree.Root, Guid.NewGuid(), limited.Id);
            db.Add(caller);
            await db.SaveChangesAsync();
        }

        var id = await UploadAsync(caller, tree.Root.Id, EnHeaders,
            Row($"{Unique()}@lms.test", "S1", role: "OrgAdmin"), Row($"{Unique()}@lms.test", "S1", role: "Counselor"));

        Assert.Contains((2, "Role", "permission_escalation"), await ErrorsAsync(caller, id));
    }

    [Fact]
    public async Task Manager_without_Staff_Import_gets_403()
    {
        var (tree, _) = await SetUpAsync("SI10");
        var manager = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var response = await _host.UploadAsync(manager, tree.Root.Id, XlsxBuilder.Create(EnHeaders, Row($"{Unique()}@lms.test", "S1")), "staff.xlsx", kind: Kind);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_created_between_preview_and_commit_makes_commit_Invalid_and_saves_nothing()
    {
        var (tree, admin) = await SetUpAsync("SI11");
        var fresh = $"{Unique()}@lms.test";
        var racing = $"{Unique("race")}@lms.test";
        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders, Row(fresh, "S1"), Row(racing, "S1"));
        Assert.Equal("Valid", (await _host.BatchAsync(_factory, id)).Status);

        // someone creates the same person between the preview and the commit
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var user = new User { UserName = racing, Email = racing, NormalizedEmail = racing.ToUpperInvariant(), DisplayNameEn = "Racer" };
            db.Users.Add(user);
            db.Set<StaffProfile>().Add(new StaffProfile(tree.School2, user.Id, "س", "Racer", racing, null, null, null, null, null, null));
            await db.SaveChangesAsync();
        }

        await CommitAsync(admin, id);

        Assert.Equal("Invalid", (await _host.BatchAsync(_factory, id)).Status);
        Assert.Equal(1, await StaffCountAsync(tree.Org.Id)); // only the racing person; nothing from the file
        Assert.Empty(_emails.To(fresh));
    }

    [Fact]
    public async Task Double_click_commit_imports_once()
    {
        var (tree, admin) = await SetUpAsync("SI12");
        var email = $"{Unique()}@lms.test";
        var id = await UploadAsync(admin, tree.Root.Id, EnHeaders, Row(email, "S1"));

        var first = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        var second = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        await _host.RunQueuedJobsAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await StaffCountAsync(tree.Org.Id));
        Assert.Single(_emails.To(email));
    }
}
