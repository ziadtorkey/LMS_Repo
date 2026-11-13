namespace Lms.Shared;

// M4b wave 3b: SCORM and H5P packages (M4b plan Task 2). The content host path /p/{token}/{**path} is not an /api route and is not listed here.
// Permission and relationship per route (the plan's route table; Task 20 probes every row):
//   POST   class-courses/{id}/packages      Packages.Upload on the ClassCourse; flag ScormContent; content host configured (409 package_host_not_configured); quota
//   GET    packages/{id}                    Packages.Upload on the package's ClassCourse (staff status read); a student is 403
//   DELETE packages/{id}                    Packages.Upload; PlacementGuard (closed class or year: 409)
//   POST   packages/{id}/module             Packages.Upload and Modules.Manage; Ready only (409 package_not_ready)
//   POST   packages/{id}/launch             Packages.View; a student enrolled in the ClassCourse and IModuleAccess; ScormContent; host configured; staff 404
//   POST   packages/{id}/preview            Packages.View; staff relationship; Ready only; a student is 403
//   POST   package-attempts/{id}/commit     Packages.View; the attempt's own student; InProgress; module visible; preview tokens refused
//   POST   package-attempts/{id}/finish     same as commit
public static partial class Routes
{
    public const string ClassCoursePackagesPattern = ClassCoursePattern + "/packages";

    public const string Packages = "/api/packages";
    public const string PackagePattern = Packages + "/{id:guid}";
    public const string PackageModulePattern = PackagePattern + "/module";
    public const string PackageLaunchPattern = PackagePattern + "/launch";
    public const string PackagePreviewPattern = PackagePattern + "/preview";

    public const string PackageAttempts = "/api/package-attempts";
    public const string PackageAttemptCommitPattern = PackageAttempts + "/{id:guid}/commit";
    public const string PackageAttemptFinishPattern = PackageAttempts + "/{id:guid}/finish";

    public static string ClassCoursePackages(Guid id) => $"{ClassCourse(id)}/packages";
    public static string Package(Guid id) => $"{Packages}/{id}";
    public static string PackageModule(Guid id) => $"{Package(id)}/module";
    public static string PackageLaunch(Guid id) => $"{Package(id)}/launch";
    public static string PackagePreview(Guid id) => $"{Package(id)}/preview";
    public static string PackageAttemptCommit(Guid id) => $"{PackageAttempts}/{id}/commit";
    public static string PackageAttemptFinish(Guid id) => $"{PackageAttempts}/{id}/finish";
}
