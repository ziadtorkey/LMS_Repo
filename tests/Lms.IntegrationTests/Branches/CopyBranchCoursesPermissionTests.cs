using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Features.Branches;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Infrastructure.Branches;
using Lms.Infrastructure.Courses;
using Lms.Infrastructure.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Branches;

/// <summary>Copy and template of courses must not be a way around course permissions (QA2 review of #319).</summary>
public sealed partial class CopyBranchCoursesTests
{
    private static readonly string[] CopyPermissions =
    [
        Permissions.OrgUnits.Create, Permissions.OrgUnits.View, Permissions.Templates.Create, Permissions.Templates.Apply,
        Permissions.Templates.View,
    ];

    private async Task<RoleAssignment> CallerWithAsync(Fixture f, params string[] permissions)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var role = new Role(f.Tree.Org.Id, null, "دور", "Copier " + Guid.NewGuid().ToString("N")[..6], RoleBaseKind.Staff);
        db.Add(role);
        db.AddRange(permissions.Select(p => new RolePermission(f.Tree.Org.Id, role.Id, p)));
        var assignment = new RoleAssignment(f.Tree.Root, Guid.NewGuid(), role.Id);
        db.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private async Task<bool> CourseExistsAsync(string code)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Course>().AnyAsync(c => c.Code == code);
    }

    [Theory]
    [InlineData(false, true)] // may read, may not create/allocate at the target
    [InlineData(true, false)] // may create/allocate, may not read at the source
    public async Task Copy_skips_courses_without_the_course_permissions(bool canWrite, bool canRead)
    {
        var f = await SetUpAsync("CBP" + (canWrite ? "W" : "R"));
        var (owned, _) = await AddCoursesAsync(f);
        var permissions = CopyPermissions.AsEnumerable();
        if (canWrite)
        {
            permissions = permissions.Concat([Permissions.Courses.Create, Permissions.Courses.Allocate]);
        }

        if (canRead)
        {
            permissions = permissions.Append(Permissions.Courses.View);
        }

        var caller = await CallerWithAsync(f, [.. permissions]);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, caller,
            new CopyBranchRequest(f.X.Id, f.Tree.School2.Id, "-P", false, false, null, null, IncludeCourses: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CopyBranchResultDto>())!;
        Assert.Contains(canWrite ? BranchExtrasPermission.CoursesViewSkipped : BranchExtrasPermission.CoursesSkipped, result.SkippedLockedSettings);
        Assert.False(await CourseExistsAsync(owned.Code + "-P"));
    }

    [Fact]
    public async Task Copy_with_all_the_course_permissions_creates_the_courses()
    {
        var f = await SetUpAsync("CBP3");
        var (owned, _) = await AddCoursesAsync(f);
        var caller = await CallerWithAsync(f, [.. CopyPermissions, Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Allocate]);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, caller,
            new CopyBranchRequest(f.X.Id, f.Tree.School2.Id, "-P", false, false, null, null, IncludeCourses: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await CourseExistsAsync(owned.Code + "-P"));
    }

    [Fact]
    public async Task Saving_a_template_with_courses_needs_courses_view()
    {
        var f = await SetUpAsync("CBP4");
        await AddCoursesAsync(f);
        var caller = await CallerWithAsync(f, CopyPermissions);

        var withCourses = await _host.SendAsync(HttpMethod.Post, Routes.Templates, caller,
            new SaveTemplateRequest(f.X.Id, "قالب", "Template", "Shared", false, false, new Dictionary<string, string>(), IncludeCourses: true));
        var withoutCourses = await _host.SendAsync(HttpMethod.Post, Routes.Templates, caller,
            new SaveTemplateRequest(f.X.Id, "قالب", "Plain", "Shared", false, false, new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Forbidden, withCourses.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withoutCourses.StatusCode);
    }

    [Fact]
    public async Task Applying_a_template_with_courses_skips_them_without_the_write_permissions()
    {
        var f = await SetUpAsync("CBP5");
        var (owned, _) = await AddCoursesAsync(f);
        var admin = await AdminAsync(f);
        var saved = await _host.SendAsync(HttpMethod.Post, Routes.Templates, admin,
            new SaveTemplateRequest(f.X.Id, "قالب", "Template", "Shared", false, false,
                new Dictionary<string, string> { ["X"] = "{CodeX}", ["Y"] = "{CodeY}" }, IncludeCourses: true));
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();
        var caller = await CallerWithAsync(f, CopyPermissions);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(id), caller,
            new ApplyTemplateRequest(f.Tree.School2.Id, new Dictionary<string, string> { ["CodeX"] = "PX", ["CodeY"] = "PY" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(BranchExtrasPermission.CoursesSkipped, (await response.Content.ReadFromJsonAsync<ApplyTemplateResultDto>())!.SkippedLockedSettings);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Course>().CountAsync(c => c.Code == owned.Code)); // only the original
    }

    [Fact]
    public async Task A_restriction_naming_an_unknown_level_type_is_rejected()
    {
        var f = await SetUpAsync("CBP6");
        await AddCoursesAsync(f);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var snapshot = await new BranchSnapshotter(db).CaptureAsync(f.X, false, false, default, includeCourses: true);
        var bad = new BranchSnapshot(snapshot.Nodes.Select(n => n with
        {
            OwnedCourses = n.OwnedCourses?.Select(c => c with
            {
                Restrictions = [new BranchRestriction(CourseRestrictionTypes.LevelType, [Guid.NewGuid().ToString()])],
            }).ToList(),
        }).ToList());
        var materializer = new BranchMaterializer(db, new OrgTree(db), new CourseCatalog(db, TestCurrentContext.PlatformAdmin()));

        await Assert.ThrowsAsync<BranchInvalidException>(() => materializer.MaterializeAsync(bad, f.Tree.School2, n => n, c => c + "-Q", default));
    }

    [Fact]
    public async Task A_branch_with_too_many_courses_is_refused()
    {
        var f = await SetUpAsync("CBP7");
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tag = Guid.NewGuid().ToString("N")[..4];
        db.AddRange(Enumerable.Range(0, BranchSnapshotter.MaxCourses + 1).Select(i => new Course(f.X, $"{tag}{i}", "م", "C", null, null, "ar", 1)));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BranchTooLargeException>(() => new BranchSnapshotter(db).CaptureAsync(f.X, false, false, default, includeCourses: true));
    }
}
