using Lms.Shared.Content;

// PROVISIONAL: the shape agreed for the wave 3b contract (plan Task 2). Delete this file when the branch is rebased onto the
// 3b contract (the same names live in Lms.Shared.Packages there) and fix up any renamed member.
namespace Lms.Shared.Packages
{
    public static class PackageKinds
    {
        public const string Scorm = "Scorm";
        public const string H5p = "H5p";
    }

    public static class PackageUploadStatuses
    {
        public const string Uploaded = "Uploaded";
        public const string Validating = "Validating";
        public const string Ready = "Ready";
        public const string Rejected = "Rejected";
    }

    public static class PackageRejectReasons
    {
        public static IReadOnlyList<string> All { get; } =
        [
            "unsafe_path", "too_many_entries", "too_large", "compression_ratio", "symlink", "duplicate_name", "bad_name_encoding",
            "executable", "encrypted", "nested_archive", "manifest_invalid", "xml_unsafe", "multi_sco", "needs_nested_frames",
            "needs_relaxed_policy", "h5p_invalid", "validation_timeout", "quota_exceeded", "scan_failed",
        ];
    }

    /// <summary>One uploaded package: its check state and, when ready, what was found. <c>LessonKey</c> is the lesson the upload was started from.</summary>
    public sealed record PackageUploadDto(
        Guid Id, string Kind, string Status, string? RejectReason, string? Title, string? Version, string? MainLibrary,
        string FileName, Guid? LessonKey);

    public sealed record AttachPackageModuleRequest(Guid LessonKey, ModuleSettingsInput Settings);
}

namespace Lms.Client.Services.Packages
{
    /// <summary>PROVISIONAL routes (the real ones come with the 3b contract).</summary>
    internal static class PackageRoutes
    {
        public static string Upload(Guid classCourseId) => $"/api/class-courses/{classCourseId}/packages";
        public static string Pending(Guid classCourseId) => $"/api/class-courses/{classCourseId}/packages?status=pending";
        public static string One(Guid id) => $"/api/packages/{id}";
        public static string Attach(Guid id) => $"/api/packages/{id}/module";
    }
}
