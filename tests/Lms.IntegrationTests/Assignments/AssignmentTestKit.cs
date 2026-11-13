using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Assignments;

/// <summary>Builders for the assignment requests and the assets a student owns.</summary>
internal static class AssignmentTestKit
{
    public static CreateCourseAssignmentRequest Request(
        string title = "Essay", string visibility = ModuleVisibilities.Published, string rule = CompletionRules.None,
        DateTime? due = null, string latePolicy = LatePolicies.Allow, int? penalty = null, string resubmission = ResubmissionRules.None,
        string target = ModuleTargets.ClassCourse, Guid? groupId = null, string? instructions = "<p>Write</p>",
        IReadOnlyList<Guid>? attachments = null, bool allowText = true, bool allowFiles = true, decimal maxGrade = 10) =>
        new(
            Lesson1,
            new ModuleSettingsInput("مهمة " + title, title, visibility, null, null, target, groupId, rule == CompletionRules.None ? CompletionRules.Submit : rule, null),
            instructions, instructions, attachments ?? [], due, maxGrade, latePolicy, penalty, resubmission, allowText, allowFiles);

    public static async Task<CourseAssignmentDto> CreateAsync(ContentTestHost t, RoleAssignment caller, ContentWorld w, CreateCourseAssignmentRequest request)
    {
        var response = await t.SendAsync(caller, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseAssignmentDto>())!;
    }

    public static Task<HttpResponseMessage> SubmitAsync(
        ContentTestHost t, RoleAssignment student, Guid assignmentId, string? text = "<p>My work</p>", IReadOnlyList<Guid>? files = null, bool draft = false) =>
        t.SendAsync(student, HttpMethod.Post, Routes.CourseAssignmentSubmit(assignmentId), new SubmitAssignmentRequest(text, files ?? [], draft));

    /// <summary>An asset of the class course that the given user created (what a student upload would leave behind).</summary>
    public static async Task<Guid> AssetOfAsync(LmsApiFactory factory, ContentWorld w, Guid createdBy)
    {
        await using var db = factory.CreateDbContext(new TestCurrentContext { UserId = createdBy, IsPlatformAdmin = true });
        var cc = await db.Set<Lms.Domain.Classes.ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        var file = new StoredFile(cc.OrganizationId, cc.OrgUnitId, cc.OrgUnitPath, "work.pdf", "application/pdf", 20, DateTime.UtcNow);
        var asset = new Asset(cc, file.Id, AssetKinds.Document, "work.pdf", "application/pdf", 20);
        db.AddRange(file, asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }
}
