namespace Lms.Shared;

// M4a wave 1: lesson modules, assets, completion and progress (M4a plan Task 1).
public static partial class Routes
{
    public const string ClassCourseLessonModulesPattern = ClassCoursePattern + "/lessons/{lessonKey:guid}/modules";
    public const string ClassCourseLessonModulesOrderPattern = ClassCourseLessonModulesPattern + "/order";
    public const string ClassCourseProgressPattern = ClassCoursePattern + "/progress";

    public static string ClassCourseLessonModules(Guid id, Guid lessonKey) => $"{ClassCourse(id)}/lessons/{lessonKey}/modules";
    public static string ClassCourseLessonModulesOrder(Guid id, Guid lessonKey) => $"{ClassCourseLessonModules(id, lessonKey)}/order";
    public static string ClassCourseProgress(Guid id) => $"{ClassCourse(id)}/progress";
    public static string ClassCourseProgressOf(Guid id, Guid studentId) => $"{ClassCourseProgress(id)}?studentId={studentId}";

    public const string Modules = "/api/modules";
    public const string ModulePattern = Modules + "/{id:guid}";
    public const string ModuleMovePattern = ModulePattern + "/move";
    public const string ModuleCompletionPattern = ModulePattern + "/completion";

    public static string Module(Guid id) => $"{Modules}/{id}";
    public static string ModuleMove(Guid id) => $"{Module(id)}/move";
    public static string ModuleCompletion(Guid id) => $"{Module(id)}/completion";

    public const string Assets = "/api/assets";
    public const string AssetsLimits = Assets + "/limits";
    public const string AssetPattern = Assets + "/{id:guid}";
    public const string AssetUrlPattern = AssetPattern + "/url";

    public static string Asset(Guid id) => $"{Assets}/{id}";
    public static string AssetUrl(Guid id) => $"{Asset(id)}/url";
    public static string AssetsLimitsFor(Guid classCourseId) => $"{AssetsLimits}?classCourseId={classCourseId}";
}
