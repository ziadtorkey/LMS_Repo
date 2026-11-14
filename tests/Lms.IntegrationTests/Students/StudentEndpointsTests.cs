using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Guardians;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Guardians.GuardianTestKit;

namespace Lms.IntegrationTests.Students;

/// <summary>M3 Task 21 (#229): student profiles: create with placement, list with relationship scope, edit, status, delete, flags, reset.</summary>
[Collection(ApiCollection.Name)]
public sealed class StudentEndpointsTests : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly GuardianTestKit _kit;
    private readonly WebApplicationFactory<Program> _host;

    public void Dispose()
    {
        _kit.Dispose();
        _host.Dispose();
    }

    public StudentEndpointsTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(_ => { });
        _kit = new GuardianTestKit(factory, _host);
    }

    private static StudentDataInput Data(string? nameEn = null, string? nationalId = null, DateOnly? dob = null, string? email = null) => new(
        "طالب " + Guid.NewGuid().ToString("N")[..4], nameEn ?? "Student " + Guid.NewGuid().ToString("N")[..8], null, null,
        dob ?? new DateOnly(2014, 3, 4), "Male", "Muslim", "EG", nationalId, null, email, null);

    private static CreateStudentRequest Create(
        Guid home, StudentDataInput? data = null, string? code = null, Guid? classId = null, string? password = null,
        IReadOnlyList<GuardianLinkInput>? guardians = null, bool confirmDuplicate = false) =>
        new(data ?? Data(), home, code, confirmDuplicate, guardians ?? [], classId, password);

    private async Task<StudentCreatedDto> CreateOkAsync(RoleAssignment who, CreateStudentRequest request)
    {
        var response = await _kit.SendAsync(HttpMethod.Post, Routes.Students, who, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StudentCreatedDto>(Web))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<StudentDetailDto> GetAsync(RoleAssignment who, Guid id) =>
        (await (await _kit.SendAsync(HttpMethod.Get, Routes.Student(id), who)).Content.ReadFromJsonAsync<StudentDetailDto>(Web))!;

    /// <summary>The account of a student (user names are platform-wide, so tests go through the profile's link).</summary>
    private static async Task<User> UserOfAsync(Lms.Infrastructure.Persistence.LmsDbContext db, Guid studentId)
    {
        var userId = (await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == studentId)).UserId;
        return await db.Users.SingleAsync(u => u.Id == userId);
    }

    private async Task<Guid> UserIdAsync(Guid studentId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await UserOfAsync(db, studentId)).Id;
    }

    private async Task SetSettingAsync(AccessTree tree, OrgUnit unit, string key, string json)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(tree.Org.Id, unit.Id, unit.Path, key, json, false));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_with_class_places_and_auto_enrolls()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST1");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var created = await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id, guardians: [NewGuardian(primary: true)]));

        Assert.False(string.IsNullOrEmpty(created.IssuedPassword));
        Assert.Equal(created.Student.Summary.StudentCode.ToLowerInvariant() + "@", created.UserName[..(created.Student.Summary.StudentCode.Length + 1)]); // D47: code@shortcode
        Assert.Equal(CredentialsDelivery.None, created.CredentialsSentTo); // no student email, the guardian has none
        var detail = created.Student;
        Assert.False(string.IsNullOrEmpty(detail.Summary.StudentCode));
        Assert.Equal(w.Class.Id, detail.Summary.CurrentClassId);
        Assert.True(detail.HasAccount);
        Assert.True(detail.MustChangePassword);
        Assert.Single(detail.Guardians);
        Assert.NotNull(detail.Summary.PrimaryGuardianNameEn);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Set<CourseEnrollment>().CountAsync(e => e.StudentId == detail.Summary.Id)); // the two mandatory courses
        var user = await UserOfAsync(db, detail.Summary.Id);
        var mine = await db.RoleAssignments.Where(a => a.UserId == user.Id && !a.IsDeleted).ToListAsync();
        Assert.True(mine.Count == 1, string.Join(",", mine.Select(a => a.RoleId + "@" + a.OrgUnitId))); // the Student role at the home unit
        // the password is never returned again
        Assert.DoesNotContain(created.IssuedPassword, await (await _kit.SendAsync(HttpMethod.Get, Routes.Student(detail.Summary.Id), admin)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Class_of_other_unit_is_400_and_nothing_is_created()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST2");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var data = Data(nameEn: "Mismatch " + Guid.NewGuid().ToString("N")[..6]);

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(w.Tree.School2.Id, data, classId: w.Class.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.class_unit_mismatch, await CodeAsync(response));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<StudentProfile>().IgnoreQueryFilters().AnyAsync(s => s.OrganizationId == w.Tree.Org.Id));
    }

    [Fact]
    public async Task Code_modes_Auto_refuses_a_typed_code_and_Manual_requires_one()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3");
        var typed = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School1.Id, code: "TYPED-1"));
        Assert.Equal(HttpStatusCode.BadRequest, typed.StatusCode);
        Assert.Equal(ProblemCodes.student_code_not_allowed, await CodeAsync(typed));

        await SetSettingAsync(tree, tree.School2, SettingKeys.StudentsCodeMode.Name, "\"Manual\"");
        var missing = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School2.Id));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(ProblemCodes.student_code_required, await CodeAsync(missing));

        var ok = await CreateOkAsync(admin, Create(tree.School2.Id, code: "MAN-" + Guid.NewGuid().ToString("N")[..6]));
        var taken = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School2.Id, code: ok.Student.Summary.StudentCode));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(ProblemCodes.student_code_taken, await CodeAsync(taken));
    }

    [Fact]
    public async Task Email_of_an_account_outside_the_client_is_409_duplicate_person_and_the_account_is_untouched()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3X");
        var email = $"outsider{Guid.NewGuid():N}"[..20] + "@lms.test";
        string hash;
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var outsider = new User { UserName = email, Email = email, DisplayNameEn = "Outsider" };
            Assert.True((await users.CreateAsync(outsider, "TheirOwn1Pass")).Succeeded);
            hash = outsider.PasswordHash!;
        }

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School1.Id, Data(email: email)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.duplicate_person, body.GetProperty("code").GetString());
        Assert.Equal("Email", body.GetProperty("field").GetString());
        using var after = _host.Services.CreateScope();
        var stored = (await after.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email))!;
        Assert.Equal(hash, stored.PasswordHash);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<StudentProfile>().IgnoreQueryFilters().AnyAsync(x => x.Email == email)); // nothing was created
    }

    [Fact]
    public async Task Code_change_renames_the_account_and_frees_the_old_code()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3C");
        await SetSettingAsync(tree, tree.School1, SettingKeys.StudentsCodeMode.Name, "\"Manual\"");
        var oldCode = "OLD-" + Guid.NewGuid().ToString("N")[..6];
        var newCode = "NEW-" + Guid.NewGuid().ToString("N")[..6];
        var created = await CreateOkAsync(admin, Create(tree.School1.Id, code: oldCode));
        var oldName = created.UserName;
        var shortCode = oldName[(oldName.IndexOf('@') + 1)..];
        Assert.Equal($"{oldCode.ToLowerInvariant()}@{shortCode}", oldName);

        var edit = await _kit.SendAsync(HttpMethod.Put, Routes.Student(created.Student.Summary.Id), admin,
            new UpdateStudentRequest(Data(), tree.School1.Id, newCode, true));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var account = (await users.FindByNameAsync($"{newCode.ToLowerInvariant()}@{shortCode}"))!; // the sign-in name follows the code
            Assert.NotNull(account);
            Assert.Null(await users.FindByNameAsync(oldName));
        }

        // the freed code can be given to another student: its sign-in name is free again
        var reuse = await CreateOkAsync(admin, Create(tree.School1.Id, code: oldCode));
        Assert.Equal(oldName, reuse.UserName);
    }

    [Fact]
    public async Task Code_change_to_a_taken_sign_in_name_is_409_and_nothing_changes()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3D");
        await SetSettingAsync(tree, tree.School1, SettingKeys.StudentsCodeMode.Name, "\"Manual\"");
        var code = "KEEP-" + Guid.NewGuid().ToString("N")[..6];
        var squatted = "SQ-" + Guid.NewGuid().ToString("N")[..6];
        var created = await CreateOkAsync(admin, Create(tree.School1.Id, code: code));
        var shortCode = created.UserName[(created.UserName.IndexOf('@') + 1)..];
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(new User { UserName = $"{squatted.ToLowerInvariant()}@{shortCode}", DisplayNameEn = "Squatter" })).Succeeded);
        }

        var edit = await _kit.SendAsync(HttpMethod.Put, Routes.Student(created.Student.Summary.Id), admin,
            new UpdateStudentRequest(Data(), tree.School1.Id, squatted, true));

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        var body = await edit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((ProblemCodes.duplicate_person, "StudentCode"), (body.GetProperty("code").GetString(), body.GetProperty("field").GetString()));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(code, (await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(x => x.Id == created.Student.Summary.Id)).StudentCode);
    }

    [Fact]
    public async Task Moving_the_home_needs_a_target_the_caller_can_reach_and_a_level_that_hosts_classes()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3M");
        var created = await CreateOkAsync(admin, Create(tree.School1.Id));
        var schoolAdmin = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        OrgUnit noClasses;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var level = new LevelType(tree.Org.Id, "مستوى", "No classes", 9, false);
            var parent = await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id);
            noClasses = OrgUnit.CreateChild(parent, level.Id, "قسم", "Admin office", "ADM" + Guid.NewGuid().ToString("N")[..4]);
            db.AddRange(level, noClasses);
            await db.SaveChangesAsync();
        }

        Task<HttpResponseMessage> Move(RoleAssignment who, Guid unit) =>
            _kit.SendAsync(HttpMethod.Put, Routes.Student(created.Student.Summary.Id), who,
                new UpdateStudentRequest(Data(), unit, created.Student.Summary.StudentCode, true));

        var invalidLevel = await Move(admin, noClasses.Id);
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.invalid_level_type), (invalidLevel.StatusCode, await CodeAsync(invalidLevel)));
        // a target outside the caller's scope does not exist for the caller
        Assert.Equal(HttpStatusCode.NotFound, (await Move(schoolAdmin, tree.School2.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Move(admin, tree.School2.Id)).StatusCode);
    }

    [Fact]
    public async Task Moving_the_home_needs_Students_Update_at_the_target_not_just_visibility()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST3U");
        var created = await CreateOkAsync(admin, Create(tree.School1.Id));
        // The caller may update students at School 1 and can see School 2, but a policy blocks Students.Update there.
        _host.Services.GetRequiredService<TestPolicyEvaluator>().Block(Permissions.Students.Update, tree.School2);

        var response = await _kit.SendAsync(HttpMethod.Put, Routes.Student(created.Student.Summary.Id), admin,
            new UpdateStudentRequest(Data(), tree.School2.Id, created.Student.Summary.StudentCode, true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Student(created.Student.Summary.Id), admin))
            .Content.ReadFromJsonAsync<StudentDetailDto>(Web))!;
        Assert.Equal(tree.School1.Id, after.Summary.HomeOrgUnitId);
    }

    [Fact]
    public async Task National_id_duplicate_is_409_and_name_plus_birth_date_needs_confirmation()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST4");
        var nationalId = UniqueNationalId();
        var data = Data("Twin Student", nationalId);
        await CreateOkAsync(admin, Create(tree.School1.Id, data));

        var sameId = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School2.Id, Data(nationalId: nationalId)));
        Assert.Equal(HttpStatusCode.Conflict, sameId.StatusCode);
        var body = await sameId.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.duplicate_person, body.GetProperty("code").GetString());
        Assert.Equal("nationalId", body.GetProperty("field").GetString());

        var twin = data with { NationalId = null, NameAr = data.NameAr };
        var possible = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School2.Id, twin));
        Assert.Equal(HttpStatusCode.Conflict, possible.StatusCode);
        Assert.Equal(ProblemCodes.possible_duplicate, await CodeAsync(possible));
        await CreateOkAsync(admin, Create(tree.School2.Id, twin, confirmDuplicate: true));
    }

    [Fact]
    public async Task Required_guardian_setting_is_enforced_and_a_failure_rolls_everything_back()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST5");
        await _kit.SetRequiredAsync(tree, tree.School1, true);
        var data = Data("Needs Guardian " + Guid.NewGuid().ToString("N")[..6]);

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School1.Id, data));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.guardian_required, await CodeAsync(response));
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.False(await db.Set<StudentProfile>().IgnoreQueryFilters().AnyAsync(s => s.OrganizationId == tree.Org.Id)); // profile, account and code rolled back
            Assert.False(await db.Users.AnyAsync(u => u.DisplayNameEn == data.NameEn));
        }

        await CreateOkAsync(admin, Create(tree.School1.Id, data, guardians: [NewGuardian(primary: true)]));
    }

    [Fact]
    public async Task Home_unit_that_cannot_host_classes_is_400_and_a_weak_password_is_400()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST6");
        OrgUnit noClasses;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var level = new LevelType(tree.Org.Id, "إدارة", "Admin level", 9, canHostClasses: false);
            noClasses = OrgUnit.CreateChild(tree.Root, level.Id, "إدارة", "Admin unit", "ADM");
            db.AddRange(level, noClasses);
            await db.SaveChangesAsync();
        }

        var hosted = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(noClasses.Id));
        var weak = await _kit.SendAsync(HttpMethod.Post, Routes.Students, admin, Create(tree.School1.Id, password: "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, hosted.StatusCode);
        Assert.Equal(ProblemCodes.invalid_level_type, await CodeAsync(hosted));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(weak));
        var chosen = await CreateOkAsync(admin, Create(tree.School1.Id, password: "Chosen9Pass"));
        Assert.Equal("Chosen9Pass", chosen.IssuedPassword);
    }

    [Fact]
    public async Task Teacher_lists_only_students_of_own_classes_and_gets_404_for_others()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST7");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var own = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id))).Student.Summary.Id;
        var other = (await CreateOkAsync(admin, Create(w.Tree.School1.Id))).Student.Summary.Id; // unplaced
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id);
            cls.Update(cls.Code, cls.NameAr, cls.NameEn, cls.Capacity, teacher.UserId); // homeroom teacher
            await db.SaveChangesAsync();
        }

        var list = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Students, teacher)).Content.ReadFromJsonAsync<PagedResult<StudentSummaryDto>>(Web))!;

        Assert.Equal([own], list.Items.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(own), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(other), teacher)).StatusCode);
        // an admin sees both
        var all = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Students, admin)).Content.ReadFromJsonAsync<PagedResult<StudentSummaryDto>>(Web))!;
        Assert.Contains(all.Items, s => s.Id == own);
        Assert.Contains(all.Items, s => s.Id == other);
    }

    [Fact]
    public async Task Teacher_sees_guardians_only_of_students_they_reach()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST7G");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacherUser = await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(_factory);
        var teacher = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, teacherUser.Id);
        var reached = await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id, guardians: [NewGuardian(primary: true, name: "Parent Reached")]));
        var other = await CreateOkAsync(admin, Create(w.Tree.School1.Id, guardians: [NewGuardian(primary: true, name: "Parent Other")])); // not enrolled with this teacher
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(w.CcA, teacherUser.Id, ClassCourseTeacher.PrimaryRole, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var reachedGuardian = reached.Student.Guardians.Single().GuardianId;
        var otherGuardian = other.Student.Guardians.Single().GuardianId;

        var list = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Guardians, teacher)).Content.ReadFromJsonAsync<PagedResult<GuardianSummaryDto>>(Web))!;
        Assert.Equal([reachedGuardian], list.Items.Select(g => g.Id));
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(reachedGuardian), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(otherGuardian), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentGuardians(reached.Student.Summary.Id), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentGuardians(other.Student.Summary.Id), teacher)).StatusCode);

        var all = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Guardians, admin)).Content.ReadFromJsonAsync<PagedResult<GuardianSummaryDto>>(Web))!;
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task Supervisor_sees_only_students_enrolled_in_a_course_they_supervise()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST7S");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var supervisorUser = await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(_factory);
        var supervisor = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Supervisor, supervisorUser.Id);
        var enrolled = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id))).Student;
        var other = (await CreateOkAsync(admin, Create(w.Tree.School1.Id))).Student; // enrolled in nothing
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(w.A, supervisorUser.Id));
            await db.SaveChangesAsync();
        }

        var list = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Students, supervisor)).Content.ReadFromJsonAsync<PagedResult<StudentSummaryDto>>(Web))!;

        Assert.Equal([enrolled.Summary.Id], list.Items.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(enrolled.Summary.Id), supervisor)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(other.Summary.Id), supervisor)).StatusCode);
    }

    [Fact]
    public async Task List_filters_by_class_unplaced_status_search_and_pages()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST8");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var placed = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, Data("Alpha Placed"), classId: w.Class.Id))).Student;
        var loose = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, Data("Beta Loose")))).Student;

        async Task<List<Guid>> IdsAsync(string query) =>
            (await (await _kit.SendAsync(HttpMethod.Get, Routes.Students + query, admin)).Content.ReadFromJsonAsync<PagedResult<StudentSummaryDto>>(Web))!
                .Items.Select(s => s.Id).ToList();

        Assert.Equal([placed.Summary.Id], await IdsAsync($"?classId={w.Class.Id}"));
        Assert.Equal([loose.Summary.Id], await IdsAsync("?unplaced=true"));
        Assert.Equal([loose.Summary.Id], await IdsAsync("?search=beta"));
        Assert.Equal([placed.Summary.Id], await IdsAsync($"?search={placed.Summary.StudentCode[..6]}"));
        Assert.Equal(2, (await IdsAsync("?pageSize=1&sort=name")).Count + 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Get, Routes.Students + "?pageSize=999", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Get, Routes.Students + "?status=Nope", admin)).StatusCode);
    }

    [Fact]
    public async Task Edit_updates_data_code_only_in_Manual_mode_and_moving_a_placed_student_is_409()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST9");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var placed = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id))).Student;
        var loose = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, guardians: [NewGuardian(primary: true)]))).Student;

        UpdateStudentRequest Edit(StudentDetailDto s, Guid home, string? code = null, string name = "Renamed Student") => new(
            Data(name) with { NameAr = s.Summary.NameAr }, home, code ?? s.Summary.StudentCode, true);

        var renamed = await _kit.SendAsync(HttpMethod.Put, Routes.Student(placed.Summary.Id), admin, Edit(placed, w.Tree.School1.Id));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Renamed Student", (await renamed.Content.ReadFromJsonAsync<StudentDetailDto>(Web))!.Summary.NameEn);

        var codeInAuto = await _kit.SendAsync(HttpMethod.Put, Routes.Student(placed.Summary.Id), admin, Edit(placed, w.Tree.School1.Id, "NEWCODE1"));
        Assert.Equal(HttpStatusCode.BadRequest, codeInAuto.StatusCode);
        Assert.Equal(ProblemCodes.student_code_not_allowed, await CodeAsync(codeInAuto));

        var movePlaced = await _kit.SendAsync(HttpMethod.Put, Routes.Student(placed.Summary.Id), admin, Edit(placed, w.Tree.School2.Id));
        Assert.Equal(HttpStatusCode.Conflict, movePlaced.StatusCode);
        Assert.Equal(ProblemCodes.already_placed, await CodeAsync(movePlaced));

        var moved = await _kit.SendAsync(HttpMethod.Put, Routes.Student(loose.Summary.Id), admin, Edit(loose, w.Tree.School2.Id));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(w.Tree.School2.Id, (await GetAsync(school2Admin, loose.Summary.Id)).Summary.HomeOrgUnitId);
        Assert.Single((await GetAsync(school2Admin, loose.Summary.Id)).Guardians); // the guardian link followed the student
    }

    [Fact]
    public async Task Deactivate_needs_a_reason_writes_history_and_follows_the_account_status()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST10");
        var student = (await CreateOkAsync(admin, Create(tree.School1.Id))).Student;
        var url = Routes.StudentStatus(student.Summary.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Put, url, admin, new SetPersonStatusRequest(PersonStatuses.Deactivated, " "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Put, url, admin, new SetPersonStatusRequest("Expired", "x"))).StatusCode);
        var ok = await _kit.SendAsync(HttpMethod.Put, url, admin, new SetPersonStatusRequest(PersonStatuses.Deactivated, "left the school"));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var detail = await GetAsync(admin, student.Summary.Id);
        Assert.Equal(PersonStatuses.Deactivated, detail.Summary.Status);
        Assert.Equal("left the school", Assert.Single(detail.StatusHistory).Reason);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(UserStatus.Deactivated, (await UserOfAsync(db, student.Summary.Id)).Status);
    }

    [Fact]
    public async Task Delete_soft_deletes_the_profile_links_placement_and_student_assignment()
    {
        var w = await EnrollmentWorld.CreateAsync(_factory, "ST11");
        var admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var created = (await CreateOkAsync(admin, Create(w.Tree.School1.Id, classId: w.Class.Id, guardians: [NewGuardian(primary: true)]))).Student;

        var response = await _kit.SendAsync(HttpMethod.Delete, Routes.Student(created.Summary.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(created.Summary.Id), admin)).StatusCode);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True((await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == created.Summary.Id)).IsDeleted);
        Assert.False(await db.Set<StudentGuardian>().AnyAsync(l => l.StudentId == created.Summary.Id));
        Assert.False(await db.Set<ClassStudent>().AnyAsync(c => c.StudentId == created.Summary.Id && c.Status == ClassStudent.ActiveStatus));
        var user = await UserOfAsync(db, created.Summary.Id);
        Assert.False(await db.RoleAssignments.AnyAsync(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task Feature_flags_accept_known_names_and_null_inherits()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST12");
        var student = (await CreateOkAsync(admin, Create(tree.School1.Id))).Student;
        var url = Routes.StudentFeatureFlags(student.Summary.Id);

        var unknown = await _kit.SendAsync(HttpMethod.Put, url, admin, new SetStudentFeatureFlagsRequest(new Dictionary<string, bool?> { ["Features.Nope"] = true }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var set = await _kit.SendAsync(HttpMethod.Put, url, admin, new SetStudentFeatureFlagsRequest(
            new Dictionary<string, bool?> { [FeatureFlags.Messaging.Name] = true, [FeatureFlags.VirtualClasses.Name] = false }));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var overrides = (await GetAsync(admin, student.Summary.Id)).FeatureFlagOverrides;
        Assert.True(overrides[FeatureFlags.Messaging.Name]);
        Assert.False(overrides[FeatureFlags.VirtualClasses.Name]);

        await _kit.SendAsync(HttpMethod.Put, url, admin, new SetStudentFeatureFlagsRequest(new Dictionary<string, bool?> { [FeatureFlags.Messaging.Name] = null }));
        Assert.DoesNotContain(FeatureFlags.Messaging.Name, (await GetAsync(admin, student.Summary.Id)).FeatureFlagOverrides.Keys);

        using var scope = _host.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<Lms.Infrastructure.People.Students.IStudentFeatureResolver>();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.NewGuid(), isPlatformAdmin: true);
        Assert.False(await resolver.IsEnabledAsync(student.Summary.Id, FeatureFlags.VirtualClasses, default)); // override false
        Assert.False(await resolver.IsEnabledAsync(student.Summary.Id, FeatureFlags.Messaging, default)); // inherited default (false)
    }

    [Fact]
    public async Task Reset_password_returns_new_password_once_and_sets_must_change()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST13");
        var created = await CreateOkAsync(admin, Create(tree.School1.Id));
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByIdAsync((await UserIdAsync(created.Student.Summary.Id)).ToString()))!;
            user.MustChangePassword = false; // the student already changed it
            await users.UpdateAsync(user);
        }

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.StudentResetPassword(created.Student.Summary.Id), admin, new ResetStudentPasswordRequest(null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reset = (await response.Content.ReadFromJsonAsync<StudentCreatedDto>(Web))!;
        Assert.NotEqual(created.IssuedPassword, reset.IssuedPassword);
        Assert.True(reset.Student.MustChangePassword);
        using var after = _host.Services.CreateScope();
        var stored = (await after.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync((await UserIdAsync(created.Student.Summary.Id)).ToString()))!;
        Assert.True(await after.ServiceProvider.GetRequiredService<UserManager<User>>().CheckPasswordAsync(stored, reset.IssuedPassword));
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentResetPassword(created.Student.Summary.Id), admin, new ResetStudentPasswordRequest("weak"))).StatusCode);
    }

    [Fact]
    public async Task Permissions_and_isolation_hold_for_every_route()
    {
        var (tree, admin) = await _kit.SetUpAsync("ST14");
        var (_, adminB) = await _kit.SetUpAsync("ST14B");
        var supervisor = await _kit.CallerWithRoleAsync(tree, tree.Root, BuiltInRoles.Supervisor);
        var school2Admin = await _kit.CallerWithRoleAsync(tree, tree.School2, BuiltInRoles.OrgAdmin);
        var student = (await CreateOkAsync(admin, Create(tree.School1.Id))).Student;
        var id = student.Summary.Id;
        var edit = new UpdateStudentRequest(Data(), tree.School1.Id, student.Summary.StudentCode, true);

        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(id), supervisor)).StatusCode); // supervises no course of this student (D42)
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Post, Routes.Students, supervisor, Create(tree.School1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Put, Routes.Student(id), supervisor, edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Delete, Routes.Student(id), supervisor)).StatusCode);
        foreach (var other in new[] { school2Admin, adminB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.Student(id), other)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Put, Routes.Student(id), other, edit)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Delete, Routes.Student(id), other)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentResetPassword(id), other, new ResetStudentPasswordRequest(null))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, Routes.Students, school2Admin, Create(tree.School1.Id))).StatusCode); // unit outside scope
    }
}
