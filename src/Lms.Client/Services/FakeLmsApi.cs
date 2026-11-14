using Lms.Shared.Authorization;
using Lms.Shared.Me;

namespace Lms.Client.Services;

/// <summary>Standalone dev fake (Lms:UseFakeApi=true) until the real Me endpoints are used.</summary>
public sealed class FakeLmsApi(IContextStore context) : ILmsApi
{
    private static readonly Guid TeacherContext = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid StudentContext = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) => Task.FromResult(new MeDto(
        Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "أحمد المدير", "Ahmed Admin", true,
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d1"), "المدرسة الرئيسية", "Main School", "مدير المنظمة", "Org Admin"),
            new(Guid.Parse("00000000-0000-0000-0000-0000000000c2"), OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d2"), "فرع الشمال", "North Branch", "معلم", "Teacher"),
            new(StudentContext, OrgId, "مدارس النور", "Al Noor Schools",
                Guid.Parse("00000000-0000-0000-0000-0000000000d2"), "فرع الشمال", "North Branch", "طالب", "Student"),
        ]));

    public Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default) =>
        Task.FromResult(new EffectivePermissionsDto(null, context.ActiveAssignmentId switch
        {
            // Dev-only role sets so the teacher and student pages can be looked at with the fake API.
            var id when id == TeacherContext =>
            [
                Permissions.LessonPlans.View, Permissions.Modules.View, Permissions.Modules.Manage, Permissions.CourseAssignments.View,
                Permissions.CourseAssignments.Manage, Permissions.CourseAssignments.Grade, Permissions.Quizzes.View, Permissions.Quizzes.Manage,
                Permissions.Quizzes.Grade, Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate, Permissions.Library.View,
            ],
            var id when id == StudentContext =>
            [
                Permissions.LessonPlans.View, Permissions.Modules.View, Permissions.CourseAssignments.View, Permissions.CourseAssignments.Submit,
                Permissions.Quizzes.View, Permissions.Quizzes.Attempt, Permissions.Forums.View, Permissions.Forums.Post, Permissions.Library.View,
            ],
            _ => Permissions.All,
        }));
}
