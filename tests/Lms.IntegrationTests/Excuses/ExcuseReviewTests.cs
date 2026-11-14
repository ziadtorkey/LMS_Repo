using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Excuses;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Excuses;

/// <summary>The review side of excuses: the counselor queue, reject, escalate and the escalation targets (M5 Task 19, FR-EXC-03).</summary>
[Collection(ApiCollection.Name)]
public sealed class ExcuseReviewTests(LmsApiFactory factory)
{
    private sealed record World(
        AccessTree Tree, RoleAssignment Counselor1, RoleAssignment Counselor2, RoleAssignment Manager, RoleAssignment Teacher, Guid CounselorUser1,
        Guid ManagerUser, Guid Guardian, Guid Excuse1, Guid Excuse2);

    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    private async Task<World> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var counselorUser1 = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var managerUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardian = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var counselor1 = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, counselorUser1);
        var counselor2 = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, managerUser);
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var excuse1 = await AddExcuseAsync(tree.School1, name + "1", guardian);
        var excuse2 = await AddExcuseAsync(tree.School2, name + "2", guardian);
        return new World(tree, counselor1, counselor2, manager, teacher, counselorUser1, managerUser, guardian, excuse1, excuse2);
    }

    private async Task<Guid> AddExcuseAsync(OrgUnit home, string code, Guid submittedBy)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, home, code);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var excuse = new AbsenceExcuse(student, submittedBy, "Family", "travel", new DateOnly(2027, 2, 4), new DateOnly(2027, 2, 4), null, DateTime.UtcNow.AddDays(-2));
        db.Add(excuse);
        await db.SaveChangesAsync();
        return excuse.Id;
    }

    private Task<HttpResponseMessage> SendAsync(RoleAssignment who, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return factory.CreateClient().SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<List<Guid>> QueueIdsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PagedResult<ExcuseReviewItemDto>>(Web))!.Items.Select(i => i.Excuse.Id).ToList();

    [Fact]
    public async Task Queue_lists_only_the_excuses_of_the_callers_subtree()
    {
        var w = await SetUpAsync("ER1");
        var other = await SetUpAsync("ER2");

        var school1 = await QueueIdsAsync(await SendAsync(w.Counselor1, HttpMethod.Get, Routes.ExcuseReviewPattern));
        var school2 = await QueueIdsAsync(await SendAsync(w.Counselor2, HttpMethod.Get, Routes.ExcuseReviewPattern));
        var root = await QueueIdsAsync(await SendAsync(w.Manager, HttpMethod.Get, Routes.ExcuseReviewPattern));
        var otherClient = await QueueIdsAsync(await SendAsync(other.Counselor1, HttpMethod.Get, Routes.ExcuseReviewPattern));

        Assert.Equal([w.Excuse1], school1);
        Assert.Equal([w.Excuse2], school2);
        Assert.Equal(new[] { w.Excuse1, w.Excuse2 }.Order(), root.Order());
        Assert.DoesNotContain(w.Excuse1, otherClient);
        Assert.DoesNotContain(w.Excuse2, otherClient);
    }

    [Fact]
    public async Task Queue_filters_by_status_and_a_foreign_unit_filter_is_404()
    {
        var w = await SetUpAsync("ER3");
        var other = await SetUpAsync("ER4");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("no"))).StatusCode);

        var pending = await QueueIdsAsync(await SendAsync(w.Manager, HttpMethod.Get, Routes.ExcuseReviewPattern));
        var rejected = await QueueIdsAsync(await SendAsync(w.Manager, HttpMethod.Get, Routes.ExcuseReviewPattern + "?status=Rejected"));
        var foreignUnit = await SendAsync(w.Counselor1, HttpMethod.Get, Routes.ExcuseReviewPattern + $"?unitId={other.Tree.School1.Id}");
        var badStatus = await SendAsync(w.Counselor1, HttpMethod.Get, Routes.ExcuseReviewPattern + "?status=Nope");

        Assert.DoesNotContain(w.Excuse1, pending);
        Assert.Contains(w.Excuse2, pending);
        Assert.Equal([w.Excuse1], rejected);
        Assert.Equal(HttpStatusCode.NotFound, foreignUnit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [Fact]
    public async Task A_teacher_cannot_review_reject_or_escalate()
    {
        var w = await SetUpAsync("ER5");

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(w.Teacher, HttpMethod.Get, Routes.ExcuseReviewPattern)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(w.Teacher, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(w.Teacher, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.ManagerUser, null))).StatusCode);
    }

    [Fact]
    public async Task Another_schools_counselor_gets_404_not_403()
    {
        var w = await SetUpAsync("ER6");

        // The excuse is outside Counselor2's subtree: it does not exist for them.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Counselor2, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(w.Counselor2, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.ManagerUser, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Counselor2, HttpMethod.Get, Routes.ExcuseEscalationTargets(w.Excuse1))).StatusCode);
    }

    [Fact]
    public async Task Reject_requires_a_comment_and_keeps_the_excuse_final()
    {
        var w = await SetUpAsync("ER7");

        var none = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("  "));
        var tooLong = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest(new string('x', 501)));
        var rejected = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("no document"));
        var again = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("again"));

        Assert.Equal((HttpStatusCode.BadRequest, "excuse_comment_required"), (none.StatusCode, await CodeAsync(none)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var dto = (await rejected.Content.ReadFromJsonAsync<ExcuseDto>(Web))!;
        Assert.Equal((ExcuseStatuses.Rejected, "no document", w.CounselorUser1), (dto.Status, dto.DecisionComment, dto.ReviewerUserId));
        Assert.Equal([ExcuseStatuses.Submitted, ExcuseStatuses.Rejected], dto.History.Select(h => h.Status));
        Assert.Equal((HttpStatusCode.Conflict, "excuse_not_pending"), (again.StatusCode, await CodeAsync(again)));
    }

    [Fact]
    public async Task The_reviewer_cannot_be_the_submitter()
    {
        var w = await SetUpAsync("ER8");
        var own = await AddExcuseAsync(w.Tree.School1, "ER8x", w.CounselorUser1); // submitted by the counselor themself

        var reject = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseReject(own), new RejectExcuseRequest("no"));
        var escalate = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(own), new EscalateExcuseRequest(w.ManagerUser, null));

        Assert.Equal((HttpStatusCode.Conflict, "excuse_reviewer_is_submitter"), (reject.StatusCode, await CodeAsync(reject)));
        Assert.Equal((HttpStatusCode.Conflict, "excuse_reviewer_is_submitter"), (escalate.StatusCode, await CodeAsync(escalate)));
    }

    [Fact]
    public async Task Escalate_goes_to_an_allowed_target_only_and_only_once()
    {
        var w = await SetUpAsync("ER9");
        var stranger = (await PeopleTestKit.AddUserAsync(factory)).Id;

        var notStaff = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(stranger, null));
        var submitter = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.Guardian, null));
        var self = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.CounselorUser1, null));
        var ok = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.ManagerUser, "needs a manager"));
        var twice = await SendAsync(w.Counselor1, HttpMethod.Post, Routes.ExcuseEscalate(w.Excuse1), new EscalateExcuseRequest(w.ManagerUser, null));

        Assert.All(new[] { notStaff, submitter, self }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = (await ok.Content.ReadFromJsonAsync<ExcuseDto>(Web))!;
        Assert.Equal((ExcuseStatuses.Escalated, w.ManagerUser), (dto.Status, dto.EscalatedToUserId));
        Assert.Equal(ExcuseStatuses.Escalated, dto.History.Last().Status);
        Assert.Equal((HttpStatusCode.Conflict, "excuse_not_pending"), (twice.StatusCode, await CodeAsync(twice)));

        // An escalated excuse is still pending: the manager decides it and it leaves the queue.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Manager, HttpMethod.Post, Routes.ExcuseReject(w.Excuse1), new RejectExcuseRequest("confirmed"))).StatusCode);
        Assert.DoesNotContain(w.Excuse1, await QueueIdsAsync(await SendAsync(w.Manager, HttpMethod.Get, Routes.ExcuseReviewPattern)));
    }

    [Fact]
    public async Task Escalation_targets_are_managers_and_counselors_in_scope_without_the_submitter_or_the_caller()
    {
        var w = await SetUpAsync("ER10");
        var otherCounselor = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id);

        var response = await SendAsync(w.Counselor1, HttpMethod.Get, Routes.ExcuseEscalationTargets(w.Excuse1));
        var targets = (await response.Content.ReadFromJsonAsync<List<EscalationTargetDto>>(Web))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(targets, t => t.UserId == w.ManagerUser && t.IsDefault);
        Assert.Contains(targets, t => t.UserId == otherCounselor.UserId);
        Assert.DoesNotContain(targets, t => t.UserId == w.CounselorUser1); // the caller
        Assert.DoesNotContain(targets, t => t.UserId == w.Guardian); // the submitter
        Assert.DoesNotContain(targets, t => t.UserId == w.Counselor2.UserId); // a counselor of the other school
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(w.Teacher, HttpMethod.Get, Routes.ExcuseEscalationTargets(w.Excuse1))).StatusCode);
    }
}
