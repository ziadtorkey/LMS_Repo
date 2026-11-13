using System.Text.Json;
using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M4;

/// <summary>A class course of the seeded school with a published three-lesson plan, its teacher and enrolled students.</summary>
public sealed record M4World(
    Guid ClassCourseId, Guid[] LessonKeys, string TeacherEmail, M4World.Student Student1, M4World.Student Student2, Guid Group1Id, Guid Group2Id)
{
    public sealed record Student(Guid Id, string UserName, string Password, string Code);
}

/// <summary>
/// Set-up for the M4a content demos through the API (nothing the scenarios are not testing is clicked): year, class, course with a
/// published blueprint, the class course, the seeded teacher, two enrolled students in two groups.
/// </summary>
public static class M4Kit
{
    public static async Task<M4World> BuildWorldAsync(ApiSetup api, string suffix)
    {
        var school = await api.UnitIdByCodeAsync("S");
        var section = await api.UnitIdByCodeAsync("KG-S1");
        var yearId = await EnsureYearAsync(api, school, section);

        var courseCode = Trim("M4" + suffix);
        var course = await api.PostAsync("/api/courses", new
        {
            code = courseCode, nameAr = "مادة " + suffix, nameEn = "Course " + suffix, language = "en",
            weeklyPeriods = 4, ownerOrgUnitId = school,
        });
        var courseId = course.TryGetProperty("summary", out var summary) ? summary.GetProperty("id").GetGuid() : course.GetProperty("id").GetGuid();
        await api.PostAsync("/api/course-allocations", new { orgUnitId = section, courseIds = new[] { courseId } });

        var draft = await api.PostAsync($"/api/courses/{courseId}/blueprints/draft", new { });
        var revision = draft.TryGetProperty("summary", out var ds) ? ds.GetProperty("revision").GetInt32() : draft.GetProperty("revision").GetInt32();
        await api.PutAsync($"/api/courses/{courseId}/blueprints/draft", new
        {
            revision,
            sections = new[]
            {
                new
                {
                    key = (Guid?)null, titleAr = "الأسبوع ١", titleEn = "Week 1",
                    lessons = new[]
                    {
                        new { key = (Guid?)null, titleAr = "الدرس ١", titleEn = "Lesson 1", objectivesAr = (string?)null, objectivesEn = (string?)null, plannedOffsetDays = (int?)0 },
                        new { key = (Guid?)null, titleAr = "الدرس ٢", titleEn = "Lesson 2", objectivesAr = (string?)null, objectivesEn = (string?)null, plannedOffsetDays = (int?)7 },
                        new { key = (Guid?)null, titleAr = "الدرس ٣", titleEn = "Lesson 3", objectivesAr = (string?)null, objectivesEn = (string?)null, plannedOffsetDays = (int?)14 },
                    },
                },
            },
        });
        await api.PostAsync($"/api/courses/{courseId}/blueprints/draft/publish", new { applyMode = "NewClassesOnly" });

        var cls = await api.PostAsync("/api/classes", new
        {
            code = Trim("C" + suffix), nameAr = "فصل " + suffix, nameEn = "Class " + suffix, orgUnitId = section,
            academicYearId = yearId, capacity = 30, homeroomTeacherUserId = (Guid?)null,
        });
        var classId = cls.GetProperty("id").GetGuid();
        await api.PostAsync("/api/class-courses/bulk-assign", new { orgUnitId = section, academicYearId = yearId, courseId, isOptional = false });
        var classCourses = await api.GetAsync($"/api/classes/{classId}/courses");
        var classCourseId = classCourses.EnumerateArray().First().GetProperty("id").GetGuid();

        var teacherUserId = await StaffUserIdAsync(api, "teacher@altagamo3.test");
        await api.PostAsync("/api/teacher-eligibility", new { orgUnitId = section, userIds = new[] { teacherUserId }, courseIds = new[] { courseId } });
        await api.PostAsync("/api/teacher-assignments/bulk", new { userId = teacherUserId, role = "Primary", classCourseIds = new[] { classCourseId } });

        var s1 = await CreateStudentAsync(api, section, classId, suffix, 1);
        var s2 = await CreateStudentAsync(api, section, classId, suffix, 2);
        var g1 = await api.PostAsync($"/api/class-courses/{classCourseId}/groups", new { nameAr = "المجموعة أ", nameEn = "Group A", matchType = (string?)null, matchValue = (string?)null });
        var g2 = await api.PostAsync($"/api/class-courses/{classCourseId}/groups", new { nameAr = "المجموعة ب", nameEn = "Group B", matchType = (string?)null, matchValue = (string?)null });
        var group1 = g1.GetProperty("id").GetGuid();
        var group2 = g2.GetProperty("id").GetGuid();
        await api.PostAsync($"/api/groups/{group1}/members", new { studentIds = new[] { s1.Id } });
        await api.PostAsync($"/api/groups/{group2}/members", new { studentIds = new[] { s2.Id } });

        var plan = await api.GetAsync($"/api/class-courses/{classCourseId}/lesson-plan");
        var lessonKeys = plan.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("lessons").EnumerateArray())
            .Select(l => l.GetProperty("key").GetGuid()).ToArray();
        return new M4World(classCourseId, lessonKeys, "teacher@altagamo3.test", s1, s2, group1, group2);
    }

    private static string Trim(string code) => code[..Math.Min(20, code.Length)];

    private static async Task<Guid> EnsureYearAsync(ApiSetup api, Guid school, Guid section)
    {
        var effective = await api.GetAsync($"/api/academic-years/effective?orgUnitId={section}");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var years = effective.ValueKind == JsonValueKind.Array ? effective : effective.TryGetProperty("years", out var y) ? y : effective;
        foreach (var year in years.EnumerateArray())
        {
            var start = DateOnly.Parse(year.GetProperty("startDate").GetString()!);
            var end = DateOnly.Parse(year.GetProperty("endDate").GetString()!);
            if (start <= today && today <= end)
            {
                return year.GetProperty("id").GetGuid();
            }
        }

        var created = await api.PostAsync("/api/academic-years", new
        {
            orgUnitId = school, nameAr = "عام " + today.Year, nameEn = "Year " + today.Year, startDate = today.AddDays(-60), endDate = today.AddDays(240), // ends before the shared E2E year that starts on 09-01 (year_overlap)
        });
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> StaffUserIdAsync(ApiSetup api, string email)
    {
        var staff = await api.GetAsync("/api/staff?pageSize=100");
        return staff.GetProperty("items").EnumerateArray().First(s => s.GetProperty("email").GetString() == email).GetProperty("userId").GetGuid();
    }

    private static async Task<M4World.Student> CreateStudentAsync(ApiSetup api, Guid section, Guid classId, string suffix, int n)
    {
        var created = await api.PostAsync("/api/students", new
        {
            data = new
            {
                nameAr = $"طالب {n} {suffix}", nameEn = $"Student {n} {suffix}", nicknameAr = (string?)null, nicknameEn = (string?)null,
                dateOfBirth = "2015-01-01", gender = "Male", religion = "Muslim", nationality = "EG", nationalId = (string?)null,
                mobile = (string?)null, email = (string?)null, photoFileId = (Guid?)null,
            },
            homeOrgUnitId = section, studentCode = (string?)null, confirmPossibleDuplicate = true, guardians = Array.Empty<object>(),
            classId, defaultPassword = "Passw0rd!" + n + "Qa", confirmOverCapacity = false,
        });
        var student = created.GetProperty("student");
        return new M4World.Student(
            student.GetProperty("summary").GetProperty("id").GetGuid(),
            created.GetProperty("userName").GetString()!, "Passw0rd!" + n + "Qa",
            student.GetProperty("summary").GetProperty("studentCode").GetString()!);
    }

    /// <summary>Creates a content module under a lesson through the API (as the signed-in session) and returns its id.</summary>
    public static async Task<Guid> AddModuleAsync(
        ApiSetup api, M4World world, int lesson, string kind, string titleEn, string rule = "None", string visibility = "Published",
        DateTime? from = null, DateTime? to = null, Guid? groupId = null, string? html = null, string? url = null, Guid? assetId = null)
    {
        var created = await api.PostAsync($"/api/class-courses/{world.ClassCourseId}/lessons/{world.LessonKeys[lesson]}/modules", new
        {
            kind, titleAr = "ع " + titleEn, titleEn, visibility, visibleFromUtc = from, visibleToUtc = to,
            target = groupId is null ? "ClassCourse" : "Group", groupId, completionRule = rule, completionMinScore = (decimal?)null,
            content = new { assetId, bodyHtmlAr = (string?)null, bodyHtmlEn = html, url },
        });
        return created.TryGetProperty("node", out var node) ? node.GetProperty("id").GetGuid() : created.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads a file as an asset of the class course (multipart, the way the uploader does) and returns the asset id.</summary>
    public static async Task<Guid> UploadAssetAsync(E2EApi api, Guid classCourseId, string fileName, string contentType, byte[] bytes)
    {
        var response = await api.SendFormAsync(
            "POST", "/api/assets", new Dictionary<string, string> { ["classCourseId"] = classCourseId.ToString() }, "file",
            new Microsoft.Playwright.FilePayload { Name = fileName, MimeType = contentType, Buffer = bytes });
        return (await api.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Forgets the session (browser storage and cookies) so the next sign-in is a different person.</summary>
    public static async Task SignOutAsync(Microsoft.Playwright.IPage page)
    {
        await page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.clear(); }");
        await page.Context.ClearCookiesAsync();
    }

    /// <summary>
    /// Signs in (through the forced password change a new student account has, D24), waits for the shell and the stored context,
    /// and switches the UI to English when asked.
    /// </summary>
    public static async Task SignInAsync(Microsoft.Playwright.IPage page, string userName, string password, string culture)
    {
        await new Lms.E2ETests.Pages.LoginPage(page).SignInAsync(userName, password);
        if (page.Url.Contains("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase))
        {
            await page.GetByTestId("change-current").FillAsync(password);
            await page.GetByTestId("change-new").FillAsync(password + "x1");
            await page.GetByTestId("change-confirm").FillAsync(password + "x1");
            await page.GetByTestId("change-submit").ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("/Account/", StringComparison.OrdinalIgnoreCase)
                                              && !url.Contains("/authentication/", StringComparison.OrdinalIgnoreCase));
        }

        var shell = new Lms.E2ETests.Pages.ShellPage(page);
        await shell.WaitReadyAsync();
        // The context is picked after the shell renders and stored: a page opened by URL reads it from local storage.
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        if (culture == "en" && await shell.HtmlLangAsync() != "en")
        {
            await shell.SwitchLanguageAsync();
        }
    }
}
