using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M2;
using Lms.E2ETests.M3;

namespace Lms.E2ETests.M4;

/// <summary>
/// Set-up through the API for the M4a scenarios (plan Task 32): a course with a published blueprint (one lesson), a class at
/// the seeded KG section with the course, the seeded Teacher assigned to it, and a student placed in the class.
/// </summary>
public static class AssessmentKit
{
    /// <summary>The student of the world with the one-time default password the account was created with.</summary>
    public sealed record StudentLogin(Guid Id, string UserName, string Password);

    public sealed record World(
        Guid ClassCourseId, Guid LessonKey, Guid ClassId, string ClassNameAr, string ClassNameEn,
        string CourseNameAr, string CourseNameEn, StudentLogin Student);

    /// <summary>Everything the scenarios need, with names carrying the run's suffix. Runs as the seeded Org Admin.</summary>
    public static async Task<World> CreateWorldAsync(E2EApi api, M2Names names)
    {
        var step = "start";
        try
        {
            return await CreateAsync(api, names, s => step = s);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException($"A JSON property is missing in step '{step}'.", ex);
        }
    }

    private static async Task<World> CreateAsync(E2EApi api, M2Names names, Action<string> at)
    {
        at("sections");
        var kg = await M3Kit.EnsureKgSectionAsync(api);
        var units = await api.JsonAsync(await api.SendAsync("GET", "/api/org-units"));
        var school = units.EnumerateArray().Single(u => u.GetProperty("code").GetString() == "S").GetProperty("id").GetGuid();

        // Course owned by the school, allocated to the KG stage, with a published blueprint of one lesson.
        at("course");
        var courseNameAr = names.NameAr("مقرر");
        var courseNameEn = names.NameEn("Course");
        var course = await api.JsonAsync(await api.SendAsync("POST", "/api/courses", new
        {
            code = $"M4-{names.Suffix}".ToUpperInvariant(), nameAr = courseNameAr, nameEn = courseNameEn, descriptionAr = (string?)null,
            descriptionEn = (string?)null, language = "en", weeklyPeriods = 3, ownerOrgUnitId = school, allocateToOrgUnitIds = new[] { kg.UnitId },
        }));
        var courseId = Prop(course, "summary", "id").GetGuid();
        at("blueprint");
        var draft = await api.JsonAsync(await api.SendAsync("POST", $"/api/courses/{courseId}/blueprints/draft"));
        var saved = await api.SendAsync("PUT", $"/api/courses/{courseId}/blueprints/draft", new
        {
            revision = Prop(draft, "summary", "revision").GetInt32(),
            sections = new[]
            {
                new
                {
                    key = (Guid?)null, titleAr = "الوحدة الأولى", titleEn = "Unit one",
                    lessons = new[] { new { key = (Guid?)null, titleAr = "الدرس الأول", titleEn = "Lesson one", objectivesAr = (string?)null, objectivesEn = (string?)null, plannedOffsetDays = (int?)null } },
                },
            },
        });
        Require(saved, "saving the blueprint draft");
        Require(await api.SendAsync("POST", $"/api/courses/{courseId}/blueprints/draft/publish", new { applyMode = "NewClassesOnly" }), "publishing the blueprint");

        // A class at the KG section; the course is assigned to it and gets the class lesson plan.
        at("class");
        var cls = await M3Kit.CreateClassAsync(api, kg, names, capacity: 30);
        Require(await api.SendAsync("POST", "/api/class-courses/bulk-assign",
            new { orgUnitId = kg.UnitId, academicYearId = kg.YearId, courseId, isOptional = false }), "assigning the course to the class");
        at("classcourses");
        var classCourses = await api.JsonAsync(await api.SendAsync("GET", $"/api/classes/{cls.ClassId}/courses"));
        var classCourseId = (classCourses.ValueKind == JsonValueKind.Array ? classCourses : classCourses.GetProperty("items"))
            .EnumerateArray().Single().GetProperty("id").GetGuid();

        // The seeded Teacher teaches it.
        at("teacher");
        var teacherUserId = await SeededTeacherUserIdAsync(api);
        Require(await api.SendAsync("POST", "/api/teacher-eligibility",
            new { orgUnitId = kg.UnitId, userIds = new[] { teacherUserId }, courseIds = new[] { courseId } }), "granting the teacher eligibility");
        Require(await api.SendAsync("POST", $"/api/class-courses/{classCourseId}/teachers", new { userId = teacherUserId, role = "Primary", from = (string?)null }), "assigning the teacher");

        // The lesson plan copy is created with the class course; its first lesson hosts the activities.
        at("plan");
        var plan = await api.JsonAsync(await api.SendAsync("GET", $"/api/class-courses/{classCourseId}/lesson-plan"));
        var lessonKey = Prop(plan, "sections").EnumerateArray().First().GetProperty("lessons").EnumerateArray().First().GetProperty("key").GetGuid();

        at("student");
        var student = await M3Kit.CreateStudentAsync(api, kg, names, "Learner", slot: 1, classId: cls.ClassId);
        return new World(classCourseId, lessonKey, cls.ClassId, cls.NameAr, cls.NameEn, courseNameAr, courseNameEn,
            new StudentLogin(student.Id, student.UserName, student.Password));
    }

    private static async Task<Guid> SeededTeacherUserIdAsync(E2EApi api)
    {
        var email = E2ESettings.SeedUser("teacher");
        var list = await api.JsonAsync(await api.SendAsync("GET", $"/api/staff?search={Uri.EscapeDataString(email)}"));
        return list.GetProperty("items").EnumerateArray()
            .First(s => string.Equals(s.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase)).GetProperty("userId").GetGuid();
    }

    /// <summary>Reads a nested property, naming the JSON when one is missing (the shapes are easy to get wrong).</summary>
    private static JsonElement Prop(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (!current.TryGetProperty(name, out current))
            {
                throw new InvalidOperationException($"No '{name}' in {element}");
            }
        }

        return current;
    }

    private static void Require(Microsoft.Playwright.IAPIResponse response, string what)
    {
        if (!response.Ok)
        {
            throw new InvalidOperationException($"{what} answered {response.Status}: {response.TextAsync().GetAwaiter().GetResult()}");
        }
    }
}
