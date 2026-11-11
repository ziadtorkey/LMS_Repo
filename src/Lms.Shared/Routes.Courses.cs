namespace Lms.Shared;

// M3 courses, allocations, restrictions, supervisors and blueprints (M3 plan Task 3).
public static partial class Routes
{
    public const string Courses = "/api/courses";
    public const string CoursePattern = Courses + "/{id:guid}";
    public const string CourseStatusPattern = CoursePattern + "/status";
    public const string CourseRestrictionsPattern = CoursePattern + "/restrictions";
    public const string CourseSupervisorsPattern = CoursePattern + "/supervisors";
    public const string CourseBlueprintsPattern = CoursePattern + "/blueprints";
    public const string CourseBlueprintVersionPattern = CourseBlueprintsPattern + "/{version:int}";
    public const string CourseBlueprintDraftPattern = CourseBlueprintsPattern + "/draft";
    public const string CourseBlueprintPublishPattern = CourseBlueprintDraftPattern + "/publish";
    public const string CourseBlueprintCopyFromPattern = CourseBlueprintDraftPattern + "/copy-from";

    public static string Course(Guid id) => $"{Courses}/{id}";
    public static string CourseStatus(Guid id) => $"{Course(id)}/status";
    public static string CourseRestrictions(Guid id) => $"{Course(id)}/restrictions";
    public static string CourseSupervisors(Guid id) => $"{Course(id)}/supervisors";
    public static string CourseBlueprints(Guid id) => $"{Course(id)}/blueprints";
    public static string CourseBlueprintVersion(Guid id, int version) => $"{CourseBlueprints(id)}/{version}";
    public static string CourseBlueprintDraft(Guid id) => $"{CourseBlueprints(id)}/draft";
    public static string CourseBlueprintPublish(Guid id) => $"{CourseBlueprintDraft(id)}/publish";
    public static string CourseBlueprintCopyFrom(Guid id) => $"{CourseBlueprintDraft(id)}/copy-from";

    public const string CourseAllocations = "/api/course-allocations";
    public const string CourseAllocationPattern = CourseAllocations + "/{id:guid}";
    public const string UnitCoursesPattern = OrgUnitPattern + "/courses";
    public const string MySupervisedCourses = Me + "/supervised-courses";

    public static string CourseAllocation(Guid id) => $"{CourseAllocations}/{id}";
    public static string UnitCourses(Guid orgUnitId) => $"{OrgUnit(orgUnitId)}/courses";
}
