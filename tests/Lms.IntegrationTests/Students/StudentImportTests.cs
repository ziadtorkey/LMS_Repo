using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Lms.Api.Features.Students.Import;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Enrollment;
using Lms.Domain.Imports;
using Lms.Domain.People;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Imports;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lms.IntegrationTests.Students;

/// <summary>M3 Task 23 (#231): student Excel import with guardians, class placement and the sensitive default-password column.</summary>
[Collection(ApiCollection.Name)]
public sealed class StudentImportTests : IDisposable
{
    private const string Kind = ImportKinds.Students;
    private const string Secret = "Sup3r-Secret-Pw9";

    private readonly LmsApiFactory _factory;
    private readonly ImportTestHost _host;

    public StudentImportTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = new ImportTestHost(factory, withTestDefinition: false, services =>
        {
            services.RemoveAll<Lms.Infrastructure.Configuration.IPolicyEvaluator>(); // the real evaluator, so the CreateStudents policy applies
            services.AddScoped<Lms.Infrastructure.Configuration.IPolicyEvaluator, Lms.Infrastructure.Configuration.PolicyEvaluator>();
            services.RemoveAll<IStudentCredentials>();
            services.AddScoped<Lms.Infrastructure.People.Students.StudentCredentials>();
            services.AddScoped<IStudentCredentials>(sp => new FailingCredentials(sp.GetRequiredService<Lms.Infrastructure.People.Students.StudentCredentials>()));
        });
    }

    /// <summary>Wraps the real credentials and throws on the Nth issue of a test (the commit has nothing else that can fail late).</summary>
    private sealed class FailingCredentials(IStudentCredentials inner) : IStudentCredentials
    {
        public static int FailAtCall; // 0 = never
        public static int Calls;

        public Task<IssuedCredentials> IssueAsync(StudentProfile student, string? requestedPassword, bool reset, CancellationToken ct)
        {
            if (FailAtCall > 0 && ++Calls == FailAtCall)
            {
                throw new InvalidOperationException("injected failure");
            }

            return inner.IssueAsync(student, requestedPassword, reset, ct);
        }

        public Task FlushDeliveriesAsync(CancellationToken ct) => inner.FlushDeliveriesAsync(ct);
    }

    public void Dispose() => _host.Dispose();

    private static readonly string[] Headers = StudentImportColumns.All.Select(c => c.HeaderEn).ToArray();

    /// <summary>One data row; unnamed columns stay empty. Keys are the column keys of the template.</summary>
    private static string?[] Row(Dictionary<string, string?> values) =>
        StudentImportColumns.All.Select(c => values.GetValueOrDefault(c.Key)).ToArray();

    private static Dictionary<string, string?> Student(string unitCode, string? name = null, string? classCode = null) => new()
    {
        ["NameAr"] = "طالب " + Guid.NewGuid().ToString("N")[..5],
        ["NameEn"] = name ?? "Imported " + Guid.NewGuid().ToString("N")[..8],
        ["DateOfBirth"] = "2014-03-04", ["Gender"] = "Male", ["Religion"] = "Muslim", ["Nationality"] = "EG",
        ["UnitCode"] = unitCode, ["ClassCode"] = classCode,
    };

    private static Dictionary<string, string?> WithGuardian(Dictionary<string, string?> row, string mobile, string? nationalId = null, int n = 1)
    {
        row[$"Guardian{n}NameAr"] = "ولي أمر";
        row[$"Guardian{n}NameEn"] = "Parent " + mobile[^4..];
        row[$"Guardian{n}Relation"] = "Father";
        row[$"Guardian{n}Mobile"] = mobile;
        row[$"Guardian{n}NationalId"] = nationalId;
        return row;
    }

    private static string Mobile() => "+2010" + Random.Shared.Next(10000000, 99999999);

    private async Task<(Guid BatchId, ImportBatchState State)> UploadAsync(RoleAssignment who, Guid unitId, params Dictionary<string, string?>[] rows)
    {
        var form = new MultipartFormDataContent { { new StringContent(unitId.ToString()), "orgUnitId" } };
        var part = new ByteArrayContent(XlsxBuilder.Create(Headers, rows.Select(Row).ToArray()));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(part, "file", "students.xlsx");
        var response = await _host.SendAsync(HttpMethod.Post, Routes.ImportsOfKind(Kind), who, form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _host.RunQueuedJobsAsync();
        return (id, await _host.BatchAsync(_factory, id));
    }

    private async Task<ImportBatchState> CommitAsync(RoleAssignment who, Guid id)
    {
        var response = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), who);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await _host.RunQueuedJobsAsync();
        return await _host.BatchAsync(_factory, id);
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        return (tree, await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    /// <summary>The class column resolves against the current (Active) year; the test world's year starts as a draft.</summary>
    private async Task ActivateAsync(EnrollmentWorld w)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<Lms.Domain.AcademicYears.AcademicYear>().SingleAsync(y => y.Id == w.Year.Id)).Activate();
        await db.SaveChangesAsync();
    }

    private async Task SetSettingAsync(AccessTree tree, string key, string json)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, key, json, false));
        await db.SaveChangesAsync();
    }

    private async Task<List<RowError>> ErrorsOfAsync(RoleAssignment who, Guid id)
    {
        var rows = (await (await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, id) + "?errorsOnly=true", who))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items");
        return rows.EnumerateArray().SelectMany(r => r.GetProperty("errors").EnumerateArray()
            .Select(e => new RowError(r.GetProperty("rowNumber").GetInt32(), e.GetProperty("column").GetString(), e.GetProperty("code").GetString()!))).ToList();
    }

    private sealed record RowError(int Row, string? Column, string Code);

    [Fact]
    public async Task Many_students_with_guardians_import_and_get_sequential_codes()
    {
        var (tree, admin) = await SetUpAsync("SI1");
        var rows = Enumerable.Range(0, 30).Select(_ => WithGuardian(Student("S1"), Mobile())).ToArray();

        var (id, state) = await UploadAsync(admin, tree.Root.Id, rows);
        Assert.Equal(("Valid", 30, 0), (state.Status, state.RowCount, state.ErrorCount));
        var committed = await CommitAsync(admin, id);

        Assert.Equal(("Committed", 30), (committed.Status, committed.CreatedCount));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var students = await db.Set<StudentProfile>().IgnoreQueryFilters().Where(s => s.OrganizationId == tree.Org.Id).ToListAsync();
        Assert.Equal(30, students.Count);
        Assert.Equal(30, students.Select(s => s.StudentCode).Distinct().Count()); // one sequence, no repeats
        Assert.All(students, s => Assert.NotNull(s.UserId));
        Assert.Equal(30, await db.Set<StudentGuardian>().IgnoreQueryFilters().CountAsync(l => l.OrganizationId == tree.Org.Id && l.IsPrimary));
    }

    [Fact]
    public async Task Siblings_in_file_share_one_guardian()
    {
        var (tree, admin) = await SetUpAsync("SI2");
        var mobile = Mobile();
        var (id, state) = await UploadAsync(admin, tree.Root.Id,
            WithGuardian(Student("S1"), mobile, "NAT1234567"), WithGuardian(Student("S1"), mobile, "NAT1234567"));
        Assert.Equal("Valid", state.Status);

        await CommitAsync(admin, id);

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<GuardianProfile>().IgnoreQueryFilters().CountAsync(g => g.OrganizationId == tree.Org.Id));
        Assert.Equal(2, await db.Set<StudentGuardian>().IgnoreQueryFilters().CountAsync(l => l.OrganizationId == tree.Org.Id));
    }

    [Fact]
    public async Task Existing_guardian_in_client_is_reused()
    {
        var (tree, admin) = await SetUpAsync("SI3");
        var mobile = Mobile();
        await using (var seed = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            seed.Add(new GuardianProfile(tree.Org.Id, "قديم", "Existing", mobile, null, null));
            await seed.SaveChangesAsync();
        }

        var (id, _) = await UploadAsync(admin, tree.Root.Id, WithGuardian(Student("S1"), mobile));
        await CommitAsync(admin, id);

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<GuardianProfile>().IgnoreQueryFilters().CountAsync(g => g.OrganizationId == tree.Org.Id));
        Assert.Equal(1, await db.Set<StudentGuardian>().IgnoreQueryFilters().CountAsync(l => l.OrganizationId == tree.Org.Id));
    }

    [Fact]
    public async Task Guardian_required_at_school_flags_rows_without_guardian()
    {
        var (tree, admin) = await SetUpAsync("SI4");
        await SetSettingAsync(tree, SettingKeys.GuardiansDataRequired.Name, "true");

        var (id, state) = await UploadAsync(admin, tree.Root.Id, Student("S1"), WithGuardian(Student("S1"), Mobile()));

        Assert.Equal("Invalid", state.Status);
        Assert.Equal([new RowError(2, "Guardian1NameAr", "guardian_required")], await ErrorsOfAsync(admin, id));
    }

    [Fact]
    public async Task Partial_guardian_group_is_reported()
    {
        var (tree, admin) = await SetUpAsync("SI5");
        var row = Student("S1");
        row["Guardian1NameEn"] = "Only a name";

        var (id, state) = await UploadAsync(admin, tree.Root.Id, row);

        Assert.Equal("Invalid", state.Status);
        var errors = await ErrorsOfAsync(admin, id);
        Assert.Contains(errors, e => e is { Code: "guardian_incomplete", Column: "Guardian1Mobile" });
        Assert.Contains(errors, e => e is { Code: "guardian_incomplete", Column: "Guardian1Relation" });
    }

    [Fact]
    public async Task Possible_duplicate_blocks_the_file()
    {
        var (tree, admin) = await SetUpAsync("SI6");
        var first = Student("S1", "Twin Student");
        first["NameAr"] = "توأم";
        var (firstId, _) = await UploadAsync(admin, tree.Root.Id, first);
        await CommitAsync(admin, firstId);

        var (id, state) = await UploadAsync(admin, tree.Root.Id, first); // same names and birth date

        Assert.Equal("Invalid", state.Status);
        Assert.Contains(await ErrorsOfAsync(admin, id), e => e is { Code: "possible_duplicate", Column: null });
    }

    [Fact]
    public async Task Manual_mode_requires_code_column_values_and_auto_mode_refuses_them()
    {
        var (tree, admin) = await SetUpAsync("SI7");
        await SetSettingAsync(tree, SettingKeys.StudentsCodeMode.Name, "\"Manual\"");
        var typed = Student("S1");
        typed["StudentCode"] = "IMP-" + Guid.NewGuid().ToString("N")[..6];

        var (missingId, missing) = await UploadAsync(admin, tree.Root.Id, Student("S1"), typed);
        Assert.Equal("Invalid", missing.Status);
        Assert.Equal([new RowError(2, "StudentCode", "student_code_required")], await ErrorsOfAsync(admin, missingId));

        var (okId, ok) = await UploadAsync(admin, tree.Root.Id, typed);
        Assert.Equal("Valid", ok.Status);
        await CommitAsync(admin, okId);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Contains(await db.Set<StudentProfile>().IgnoreQueryFilters().Select(s => s.StudentCode).ToListAsync(), c => c == typed["StudentCode"]);

        var (taken, takenState) = await UploadAsync(admin, tree.Root.Id, typed.Renamed("Other One"));
        Assert.Equal("Invalid", takenState.Status);
        Assert.Contains(await ErrorsOfAsync(admin, taken), e => e.Code == "student_code_taken");
    }

    [Fact]
    public async Task Failure_on_last_row_rolls_back_codes_guardians_accounts_placements_and_enrollments()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "SI8");
        await ActivateAsync(w);
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var (id, state) = await UploadAsync(admin, w.Tree.Root.Id,
            WithGuardian(Student("S1", classCode: w.Class.Code), Mobile()),
            WithGuardian(Student("S1", classCode: w.Class.Code), Mobile()),
            WithGuardian(Student("S1", classCode: w.Class.Code), Mobile()));
        Assert.Equal("Valid", state.Status);

        ImportBatchState failed;
        try
        {
            FailingCredentials.Calls = 0;
            FailingCredentials.FailAtCall = 3; // the last row's account fails after the first two students were fully created
            failed = await CommitAsync(admin, id);
        }
        finally
        {
            FailingCredentials.FailAtCall = 0;
        }

        Assert.Equal("Failed", failed.Status);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(0, await db.Set<StudentProfile>().IgnoreQueryFilters().CountAsync(s => s.OrganizationId == w.Tree.Org.Id));
        Assert.Equal(0, await db.Set<GuardianProfile>().IgnoreQueryFilters().CountAsync(g => g.OrganizationId == w.Tree.Org.Id));
        Assert.Equal(0, await db.Set<ClassStudent>().IgnoreQueryFilters().CountAsync(c => c.OrganizationId == w.Tree.Org.Id));
        Assert.Equal(0, await db.Set<CourseEnrollment>().IgnoreQueryFilters().CountAsync(e => e.OrganizationId == w.Tree.Org.Id));
        Assert.Equal(0, await db.Users.CountAsync(u => u.UserName!.EndsWith("@" + w.Tree.Org.ShortCode)));
    }

    [Fact]
    public async Task Rows_with_class_code_are_placed_and_auto_enrolled()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "SI9");
        await ActivateAsync(w);
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var (id, state) = await UploadAsync(admin, w.Tree.Root.Id, Student("S1", classCode: w.Class.Code), Student("S1", classCode: w.Class.Code.ToLowerInvariant()));
        Assert.Equal("Valid", state.Status);
        await CommitAsync(admin, id);

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Set<ClassStudent>().IgnoreQueryFilters().CountAsync(c => c.ClassId == w.Class.Id && c.Status == ClassStudent.ActiveStatus));
        Assert.Equal(4, await db.Set<CourseEnrollment>().IgnoreQueryFilters().CountAsync(e => e.OrganizationId == w.Tree.Org.Id)); // two mandatory courses each
    }

    [Fact]
    public async Task Unknown_closed_full_or_misplaced_class_code_blocks_the_file()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "SI10");
        await ActivateAsync(w);
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var closed = await AcademicTestKit.AddClassAsync(_factory, w.Tree.School1, w.Year);
        var elsewhere = await AcademicTestKit.AddClassAsync(_factory, w.Tree.School2, w.Year);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == closed.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        await SetSettingAsync(w.Tree, SettingKeys.ClassesCapacityMode.Name, "\"Block\"");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tiny = await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id);
            tiny.Update(tiny.Code, tiny.NameAr, tiny.NameEn, 1, null);
            await db.SaveChangesAsync();
        }

        var (id, state) = await UploadAsync(admin, w.Tree.Root.Id,
            Student("S1", classCode: "NOPE"), Student("S1", classCode: closed.Code), Student("S1", classCode: elsewhere.Code),
            Student("S1", classCode: w.Class.Code), Student("S1", classCode: w.Class.Code));

        Assert.Equal("Invalid", state.Status);
        Assert.Equal(
            [
                new RowError(2, "ClassCode", "unknown_class"), new RowError(3, "ClassCode", "class_closed"),
                new RowError(4, "ClassCode", "class_unit_mismatch"), new RowError(6, "ClassCode", "class_full"),
            ],
            await ErrorsOfAsync(admin, id));
    }

    [Fact]
    public async Task CreateStudents_policy_blocks_rows_under_the_unit()
    {
        var (tree, admin) = await SetUpAsync("SI16");
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitPolicy(tree.Org.Id, tree.School1.Id, tree.School1.Path, PolicyCatalog.CreateStudents));
            await db.SaveChangesAsync();
        }

        var (id, state) = await UploadAsync(admin, tree.Root.Id, Student("S1"), Student("S2"));

        Assert.Equal("Invalid", state.Status);
        Assert.Equal([new RowError(2, "UnitCode", "blocked_by_policy")], await ErrorsOfAsync(admin, id)); // the other school is fine
    }

    [Fact]
    public async Task Columns_endpoint_marks_DefaultPassword_sensitive()
    {
        var (_, admin) = await SetUpAsync("SI11");

        var columns = await (await _host.SendAsync(HttpMethod.Get, Routes.ImportColumns(Kind), admin)).Content.ReadFromJsonAsync<List<ImportColumnDto>>();

        Assert.Equal(["DefaultPassword"], columns!.Where(c => c.Sensitive).Select(c => c.Key));
    }

    [Fact]
    public async Task Default_password_column_is_masked_in_rows_api_preview_and_error_report()
    {
        var (tree, admin) = await SetUpAsync("SI12");
        var good = Student("S1");
        good["DefaultPassword"] = Secret;
        var bad = Student("S1");
        bad["DefaultPassword"] = Secret + "-2";
        bad["Gender"] = "Robot"; // an invalid row, so an error report exists

        var (id, state) = await UploadAsync(admin, tree.Root.Id, good, bad);

        Assert.Equal("Invalid", state.Status);
        var rowsBody = await (await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, id), admin)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, rowsBody);
        var items = JsonSerializer.Deserialize<JsonElement>(rowsBody).GetProperty("items");
        Assert.All(items.EnumerateArray(), r => Assert.Equal(ImportMasks.Sensitive, r.GetProperty("values").GetProperty("DefaultPassword").GetString()));

        var report = await _host.SendAsync(HttpMethod.Get, Routes.ImportErrorReport(Kind, id), admin);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var workbook = new XLWorkbook(await report.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet(1);
        var column = Headers.ToList().IndexOf("Default password") + 1;
        Assert.Equal(ImportMasks.Sensitive, sheet.Cell(2, column).GetString());
        Assert.Equal(ImportMasks.Sensitive, sheet.Cell(3, column).GetString());
        Assert.DoesNotContain(Secret, string.Join('|', sheet.CellsUsed().Select(c => c.GetString())));
    }

    [Fact]
    public async Task Default_password_is_not_in_import_row_values_json_and_the_source_file_is_deleted_after_commit()
    {
        var (tree, admin) = await SetUpAsync("SI13");
        var row = Student("S1");
        row["DefaultPassword"] = Secret;
        var (id, state) = await UploadAsync(admin, tree.Root.Id, row);
        Assert.Equal("Valid", state.Status);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<ImportRow>().Where(r => r.BatchId == id).Select(r => r.ValuesJson).ToListAsync();
            Assert.DoesNotContain(Secret, string.Join('|', stored));
        }

        await CommitAsync(admin, id);

        await using var after = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var batch = await after.Set<ImportBatch>().IgnoreQueryFilters().SingleAsync(b => b.Id == id);
        Assert.All(await after.Set<Lms.Domain.Files.StoredFile>().IgnoreQueryFilters().Where(f => f.Id == batch.SourceFileId).ToListAsync(), f => Assert.True(f.IsDeleted));
        Assert.DoesNotContain(Secret, string.Join('|', await after.Set<ImportRow>().Where(r => r.BatchId == id).Select(r => r.ValuesJson).ToListAsync()));
        // the account really got the chosen password
        var code = (await after.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(x => x.OrganizationId == tree.Org.Id)).StudentCode;
        var user = await after.Users.SingleAsync(u => u.UserName == code.ToLowerInvariant() + "@" + tree.Org.ShortCode);
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task Credentials_sheet_downloads_once_and_only_for_requester()
    {
        var (tree, admin) = await SetUpAsync("SI14");
        var other = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var withPassword = Student("S1");
        withPassword["DefaultPassword"] = Secret;
        var (id, _) = await UploadAsync(admin, tree.Root.Id, withPassword, Student("S1"));
        var url = $"/api/imports/{Kind}/{id}/credentials";
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, url, admin)).StatusCode); // not committed yet
        await CommitAsync(admin, id);

        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, url, other)).StatusCode); // another admin, and it is not used up
        var sheet = await _host.SendAsync(HttpMethod.Get, url, admin);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        using var workbook = new XLWorkbook(await sheet.Content.ReadAsStreamAsync());
        var cells = workbook.Worksheet(1);
        Assert.Equal(3, cells.LastRowUsed()!.RowNumber()); // header + two students
        var passwords = new[] { cells.Cell(2, 5).GetString(), cells.Cell(3, 5).GetString() };
        Assert.Contains(Secret, passwords);
        Assert.All(passwords, p => Assert.False(string.IsNullOrEmpty(p)));
        Assert.Contains("@" + tree.Org.ShortCode, cells.Cell(2, 4).GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, url, admin)).StatusCode); // once
    }

    [Fact]
    public async Task A_weak_default_password_blocks_the_row_without_echoing_it()
    {
        var (tree, admin) = await SetUpAsync("SI15");
        var row = Student("S1");
        row["DefaultPassword"] = "abc";

        var (id, state) = await UploadAsync(admin, tree.Root.Id, row);

        Assert.Equal("Invalid", state.Status);
        Assert.Equal([new RowError(2, "DefaultPassword", "invalid_password")], await ErrorsOfAsync(admin, id));
    }
}

internal static class DictionaryWith
{
    public static Dictionary<string, string?> Renamed(this Dictionary<string, string?> row, string name)
    {
        var copy = new Dictionary<string, string?>(row) { ["NameEn"] = name, ["NameAr"] = "آخر " + Guid.NewGuid().ToString("N")[..4] };
        return copy;
    }
}
