using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.UnitTests.Contracts;

/// <summary>M4b wave 3b contract (plan Task 2): package catalogs, DTOs, tracking limits, routes, permissions and codes.</summary>
public sealed class PackageContractTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Catalogs_match_the_plan_and_are_unique()
    {
        Assert.Equal(["Scorm", "H5p"], PackageKinds.All);
        Assert.Equal(["Uploaded", "Validating", "Ready", "Rejected"], PackageUploadStatuses.All);
        Assert.Equal(["Scorm12", "Scorm2004"], ScormVersions.All);
        Assert.Equal(["InProgress", "Completed"], PackageAttemptStatuses.All);
        Assert.Equal(["Highest", "Last", "First"], PackageGradingMethods.All);
        Assert.Equal(["Scorm", "Xapi"], PackageCommitFormats.All);
        Assert.Equal(ModuleKinds.Scorm, PackageKinds.Scorm);
        Assert.Equal(ModuleKinds.H5p, PackageKinds.H5p);
        Assert.All(new[] { PackageKinds.All, PackageUploadStatuses.All, ScormVersions.All, PackageRejectReasons.All },
            list => Assert.Equal(list.Count, list.Distinct().Count()));
    }

    [Fact]
    public void Reject_reasons_are_the_plan_list_plus_invalid_archive_and_bad_name_and_equal_their_names()
    {
        string[] plan =
        [
            "unsafe_path", "too_many_entries", "too_large", "compression_ratio", "symlink", "duplicate_name", "bad_name_encoding",
            "executable", "encrypted", "nested_archive", "manifest_invalid", "xml_unsafe", "multi_sco", "needs_nested_frames",
            "needs_relaxed_policy", "h5p_invalid", "validation_timeout", "quota_exceeded", "scan_failed",
        ];
        Assert.Equal([.. plan, "invalid_archive", "bad_name"], PackageRejectReasons.All);

        var fields = typeof(PackageRejectReasons).GetFields().Where(f => f.IsLiteral).ToList();
        Assert.Equal(fields.Count, PackageRejectReasons.All.Count);
        Assert.All(fields, f => Assert.Equal(f.Name, (string)f.GetRawConstantValue()!));
    }

    [Fact]
    public void Tracking_limits_and_key_lists_match_the_plan()
    {
        Assert.Equal((8192, 4096, 65536), (PackageTrackingLimits.MaxMessageBytes, PackageTrackingLimits.MaxValueBytes, PackageTrackingLimits.MaxCmiBytes));
        Assert.All(new[] { ScormCmiKeys.Scorm12, ScormCmiKeys.Scorm2004, ScormCmiKeys.Xapi }, l => Assert.Equal(l.Count, l.Distinct().Count()));
    }

    [Theory]
    [InlineData("Scorm", "Scorm12", "cmi.core.lesson_status", true)]
    [InlineData("Scorm", "Scorm12", "cmi.objectives.3.score.raw", true)]
    [InlineData("Scorm", "Scorm12", "cmi.interactions.12.student_response", true)]
    [InlineData("Scorm", "Scorm12", "cmi.core.student_name", false)]
    [InlineData("Scorm", "Scorm12", "cmi.launch_data", false)]
    [InlineData("Scorm", "Scorm12", "cmi.completion_status", false)]
    [InlineData("Scorm", "Scorm2004", "cmi.completion_status", true)]
    [InlineData("Scorm", "Scorm2004", "cmi.objectives.0.success_status", true)]
    [InlineData("Scorm", "Scorm2004", "cmi.learner_name", false)]
    [InlineData("Scorm", "Scorm2004", "adl.nav.request", false)]
    [InlineData("Scorm", "Scorm2004", "cmi.objectives.x.id", false)]
    [InlineData("Xapi", null, "result.score.scaled", true)]
    [InlineData("Xapi", null, "actor.name", false)]
    public void Cmi_allow_list_accepts_only_writable_elements_of_the_version(string format, string? version, string key, bool allowed) =>
        Assert.Equal(allowed, ScormCmiKeys.IsAllowed(format, version, key));

    [Theory]
    [InlineData(ModuleKinds.Scorm)]
    [InlineData(ModuleKinds.H5p)]
    public void Packages_use_none_submit_or_score_as_completion_rule(string kind)
    {
        Assert.Equal(["None", "Submit", "Score"], CompletionRules.ForActivity(kind));
        Assert.All(CompletionRules.ForActivity(kind), r => Assert.Contains(r, CompletionRules.All));
    }

    [Fact]
    public void Package_routes_match_the_plan()
    {
        Assert.Equal("/api/class-courses/{id:guid}/packages", Routes.ClassCoursePackagesPattern);
        Assert.Equal("/api/packages", Routes.Packages);
        Assert.Equal("/api/packages/{id:guid}", Routes.PackagePattern);
        Assert.Equal("/api/packages/{id:guid}/module", Routes.PackageModulePattern);
        Assert.Equal("/api/packages/{id:guid}/launch", Routes.PackageLaunchPattern);
        Assert.Equal("/api/packages/{id:guid}/preview", Routes.PackagePreviewPattern);
        Assert.Equal("/api/package-attempts/{id:guid}/commit", Routes.PackageAttemptCommitPattern);
        Assert.Equal("/api/package-attempts/{id:guid}/finish", Routes.PackageAttemptFinishPattern);
        Assert.Equal($"/api/class-courses/{Id}/packages", Routes.ClassCoursePackages(Id));
        Assert.Equal($"/api/class-courses/{Id}/packages?lessonKey={Other}", Routes.ClassCoursePackagesOfLesson(Id, Other));
        Assert.Equal($"/api/packages/{Id}", Routes.Package(Id));
        Assert.Equal($"/api/packages/{Id}/module", Routes.PackageModule(Id));
        Assert.Equal($"/api/packages/{Id}/launch", Routes.PackageLaunch(Id));
        Assert.Equal($"/api/packages/{Id}/preview", Routes.PackagePreview(Id));
        Assert.Equal($"/api/package-attempts/{Id}/commit", Routes.PackageAttemptCommit(Id));
        Assert.Equal($"/api/package-attempts/{Id}/finish", Routes.PackageAttemptFinish(Id));
    }

    [Fact]
    public void Upload_form_fields_are_stable()
    {
        Assert.Equal(["file", "lessonKey", "kind"], [PackageUploadForm.File, PackageUploadForm.LessonKey, PackageUploadForm.Kind]);
    }

    [Fact]
    public void The_content_host_path_is_not_an_api_route()
    {
        var all = typeof(Routes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

        Assert.DoesNotContain(all, r => r.StartsWith("/p/", StringComparison.Ordinal));
    }

    [Fact]
    public void Package_permissions_and_codes_exist()
    {
        Assert.Equal(["Packages.Upload", "Packages.View"], [Permissions.Packages.Upload, Permissions.Packages.View]);
        Assert.Equal(
            ["package_invalid", "package_too_large", "package_not_ready", "package_commit_rejected", "package_host_not_configured"],
            [ProblemCodes.package_invalid, ProblemCodes.package_too_large, ProblemCodes.package_not_ready, ProblemCodes.package_commit_rejected,
             ProblemCodes.package_host_not_configured]);
    }

    [Fact]
    public void Package_relationships_restrict_teacher_and_student()
    {
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Packages.Upload));
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Packages.View));
        Assert.Equal(Relationships.OwnEnrollments, RelationshipRules.For(BuiltInRoles.Student, Permissions.Packages.View));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Student, Permissions.Packages.Upload));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Supervisor, Permissions.Packages.View));
    }

    [Fact]
    public void Package_dtos_round_trip()
    {
        var now = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var module = new ModuleSettingsInput("حزمة", "Package", "Published", null, null, "Class", null, "Score", 80m);

        RoundTrip(new PackageUploadDto(Id, Other, Id, "course.zip", PackageKinds.Scorm, PackageUploadStatuses.Ready, null, "Course", ScormVersions.Scorm12, null, null, now));
        RoundTrip(new PackageUploadDto(Id, Other, Id, "quiz.h5p", PackageKinds.H5p, PackageUploadStatuses.Rejected, PackageRejectReasons.invalid_archive, null, null, "H5P.Foo", Other, now));
        RoundTrip(new PackageListQuery(Id, PackageUploadStatuses.Validating));
        RoundTrip(new AttachPackageModuleRequest(module));
        RoundTrip(new AttachPackageModuleRequest(module, PackageGradingMethods.First));
        Assert.Equal(PackageGradingMethods.Highest, new AttachPackageModuleRequest(module).GradingMethod);
        RoundTrip(new PackageLaunchDto("https://content.example/p/token/index.html", Id, now, now, true));
        RoundTrip(new PackagePreviewDto("https://content.example/p/token/index.html", now, now));
        RoundTrip(new PackageCommitRequest(7, PackageCommitFormats.Scorm, new Dictionary<string, string> { ["cmi.core.lesson_status"] = "completed" }));
        RoundTrip(new PackageFinishRequest(7));
        RoundTrip(new PackageAttemptDto(Id, PackageAttemptStatuses.Completed, true, 80m, 0m, 100m, 7, now));
    }

    private static void RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, Web);

        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(json, Web), Web));
    }
}
