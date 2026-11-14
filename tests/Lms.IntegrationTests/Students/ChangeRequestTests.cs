using System.Net;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Lms.IntegrationTests.Students.SelfServiceKit;

namespace Lms.IntegrationTests.Students;

/// <summary>M3 Task 22 (#230), D27 / FR-STU-04: requests for critical fields and the approval queue.</summary>
[Collection(ApiCollection.Name)]
public sealed class ChangeRequestTests : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly SelfServiceKit _kit;

    public void Dispose() => _kit.Dispose();

    public ChangeRequestTests(LmsApiFactory factory)
    {
        _factory = factory;
        _kit = new SelfServiceKit(factory, factory.WithWebHostBuilder(_ => { }));
    }

    private static CreateChangeRequestRequest NameChange(string nameEn = "New Name", string? note = null) =>
        new(Changes((StudentFields.NameEn, nameEn)), note);

    private async Task<StudentChangeRequestDto> FileAsync(SelfServiceKit.World w, CreateChangeRequestRequest? body = null)
    {
        var response = await _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me, body ?? NameChange());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await _kit.ReadAsync<StudentChangeRequestDto>(response);
    }

    [Fact]
    public async Task Student_requests_name_change_and_school_manager_is_notified()
    {
        var w = await _kit.SetUpAsync("CR1");
        var manager = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid()); // above the school
        var otherSchoolCounselor = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School2, BuiltInRoles.Counselor, Guid.NewGuid());

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me, NameChange("Renamed Student", "typo"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await _kit.ReadAsync<StudentChangeRequestDto>(response);
        Assert.Equal(("Pending", "typo", w.Student.Id), (dto.Status, dto.Note, dto.StudentId));
        var change = Assert.Single(dto.Changes);
        Assert.Equal((StudentFields.NameEn, w.Student.NameEn, "Renamed Student"), (change.Field, change.Before, change.After));
        Assert.Equal(w.Student.NameEn, (await _kit.ReloadAsync(w.Student.Id)).NameEn); // nothing applied yet
        Assert.Single(await _kit.NotificationsOfAsync(manager.UserId, NotificationEvents.StudentsChangeRequested));
        Assert.Empty(await _kit.NotificationsOfAsync(otherSchoolCounselor.UserId, NotificationEvents.StudentsChangeRequested));
    }

    [Fact]
    public async Task Request_needs_requestable_fields_a_real_change_and_valid_values()
    {
        var w = await _kit.SetUpAsync("CR2");

        async Task<HttpResponseMessage> Post(CreateChangeRequestRequest b) => await _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me, b);

        var editable = await Post(new CreateChangeRequestRequest(Changes((StudentFields.Mobile, "+201000000001")), null)); // not critical: edit it directly
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.critical_field), (editable.StatusCode, await _kit.CodeAsync(editable)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new CreateChangeRequestRequest(Changes(), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(NameChange(w.Student.NameEn))).StatusCode); // same value: no change
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new CreateChangeRequestRequest(Changes((StudentFields.DateOfBirth, "not-a-date")), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new CreateChangeRequestRequest(Changes((StudentFields.Gender, "Robot")), null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(NameChange(new string('x', 201)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(NameChange(note: new string('n', 1001)))).StatusCode);
    }

    [Fact]
    public async Task Second_pending_request_is_409()
    {
        var w = await _kit.SetUpAsync("CR3");
        await FileAsync(w);

        var second = await _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me, NameChange("Another"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ProblemCodes.change_request_pending, await _kit.CodeAsync(second));
    }

    [Fact]
    public async Task Two_simultaneous_requests_leave_exactly_one_pending()
    {
        var w = await _kit.SetUpAsync("CR4");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me, NameChange("Racer " + i))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Student_lists_own_requests_newest_first_and_cancels_a_pending_one()
    {
        var w = await _kit.SetUpAsync("CR5");
        var first = await FileAsync(w);
        Assert.Equal(HttpStatusCode.NoContent, (await _kit.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(first.Id), w.Me)).StatusCode);
        var second = await FileAsync(w, NameChange("Second try"));

        var list = await _kit.ReadAsync<List<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.MyChangeRequests, w.Me));

        Assert.Equal([second.Id, first.Id], list.Select(r => r.Id));
        Assert.Equal("Cancelled", list[1].Status);
        var again = await _kit.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(first.Id), w.Me);
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.change_request_closed), (again.StatusCode, await _kit.CodeAsync(again)));
    }

    [Fact]
    public async Task A_student_cannot_cancel_or_list_another_students_request()
    {
        var a = await _kit.SetUpAsync("CR6");
        var b = await _kit.SetUpAsync("CR6B");
        var request = await FileAsync(a);

        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(request.Id), b.Me)).StatusCode);
        Assert.Empty(await _kit.ReadAsync<List<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.MyChangeRequests, b.Me)));
    }

    [Fact]
    public async Task A_student_cannot_cancel_or_read_the_request_of_another_student_of_the_same_unit()
    {
        var a = await _kit.SetUpAsync("CR5S");
        // a second student of the same school, with a login of their own
        var other = await _kit.Kit.StudentAsync(a.Tree.School1);
        var otherUser = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(x => x.Id == other.Id)).LinkUser(otherUser);
            await db.SaveChangesAsync();
        }

        var b = await AccessTestData.AssignAsync(_factory, a.Tree, a.Tree.School1, BuiltInRoles.Student, otherUser);
        var request = await FileAsync(new SelfServiceKit.World(a.Tree, a.Admin, other, b), NameChange("B wants this"));

        Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(request.Id), a.Me)).StatusCode);
        Assert.Empty(await _kit.ReadAsync<List<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.MyChangeRequests, a.Me)));
        // B's request is untouched and still B's to cancel
        Assert.Equal(HttpStatusCode.NoContent, (await _kit.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(request.Id), b)).StatusCode);
    }

    [Fact]
    public async Task Counselor_approves_and_student_profile_changes_and_student_is_notified()
    {
        var w = await _kit.SetUpAsync("CR7");
        var request = await FileAsync(w, NameChange("Approved Name"));
        var counselor = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Counselor, Guid.NewGuid());

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), counselor, new DecideChangeRequestRequest("ok"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decided = await _kit.ReadAsync<StudentChangeRequestDto>(response);
        Assert.Equal(("Approved", "ok"), (decided.Status, decided.DecisionComment));
        Assert.NotNull(decided.DecidedAt);
        Assert.Equal("Approved Name", (await _kit.ReloadAsync(w.Student.Id)).NameEn);
        var note = Assert.Single(await _kit.NotificationsOfAsync(w.Me.UserId, NotificationEvents.StudentsChangeRequestDecided));
        Assert.Contains("Approved", note.ParametersJson);

        var again = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), counselor, new DecideChangeRequestRequest(null));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.change_request_closed), (again.StatusCode, await _kit.CodeAsync(again)));
        Assert.Equal(HttpStatusCode.Conflict, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), counselor, new DecideChangeRequestRequest("late"))).StatusCode);
        // a decided request frees the student to file a new one
        await FileAsync(w, NameChange("Next"));
    }

    [Fact]
    public async Task Reject_requires_comment_and_leaves_the_profile_alone()
    {
        var w = await _kit.SetUpAsync("CR8");
        var request = await FileAsync(w);
        var counselor = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Counselor, Guid.NewGuid());

        var blank = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), counselor, new DecideChangeRequestRequest("  "));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), counselor, new DecideChangeRequestRequest(null))).StatusCode);

        var rejected = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), counselor, new DecideChangeRequestRequest("not the legal name"));

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Rejected", (await _kit.ReadAsync<StudentChangeRequestDto>(rejected)).Status);
        Assert.Equal(w.Student.NameEn, (await _kit.ReloadAsync(w.Student.Id)).NameEn);
        Assert.Single(await _kit.NotificationsOfAsync(w.Me.UserId, NotificationEvents.StudentsChangeRequestDecided));
    }

    [Fact]
    public async Task Stale_request_is_409_when_admin_edited_field_meanwhile()
    {
        var w = await _kit.SetUpAsync("CR9");
        var request = await FileAsync(w);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == w.Student.Id);
            stored.Update(stored.NameAr, "Edited By Admin", null, null, stored.DateOfBirth, stored.Gender, stored.Religion, stored.Nationality, null, null, null, null);
            await db.SaveChangesAsync();
        }

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), w.Admin, new DecideChangeRequestRequest(null));

        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.change_request_stale), (response.StatusCode, await _kit.CodeAsync(response)));
        Assert.Equal("Edited By Admin", (await _kit.ReloadAsync(w.Student.Id)).NameEn);
        Assert.True((await _kit.ReadAsync<StudentChangeRequestDto>(await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequest(request.Id), w.Admin))).Status == "Pending");
    }

    [Fact]
    public async Task Approving_national_id_that_now_duplicates_is_409()
    {
        var w = await _kit.SetUpAsync("CR10");
        var nationalId = "N" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var request = await FileAsync(w, new CreateChangeRequestRequest(Changes((StudentFields.NationalId, nationalId)), null));
        var other = await _kit.Kit.StudentAsync(w.Tree.School2);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == other.Id);
            stored.Update(stored.NameAr, stored.NameEn, null, null, stored.DateOfBirth, stored.Gender, stored.Religion, stored.Nationality, nationalId, null, null, null);
            await db.SaveChangesAsync();
        }

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), w.Admin, new DecideChangeRequestRequest(null));

        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.duplicate_person), (response.StatusCode, await _kit.CodeAsync(response)));
        Assert.Null((await _kit.ReloadAsync(w.Student.Id)).NationalId);
    }

    [Fact]
    public async Task Queue_lists_pending_requests_of_the_unit_and_filters_by_status()
    {
        var w = await _kit.SetUpAsync("CR11");
        var other = await _kit.SetUpAsync("CR11B"); // another client
        var request = await FileAsync(w);
        await FileAsync(other);

        var pending = await _kit.ReadAsync<PagedResult<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests + "?status=Pending", w.Admin));
        var rejected = await _kit.ReadAsync<PagedResult<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests + "?status=Rejected", w.Admin));
        var all = await _kit.ReadAsync<PagedResult<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests, w.Admin));
        var bad = await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests + "?status=Nope", w.Admin);
        var school2 = await _kit.ReadAsync<PagedResult<StudentChangeRequestDto>>(
            await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests + "?orgUnitId=" + w.Tree.School2.Id, w.Admin));

        Assert.Equal([request.Id], pending.Items.Select(r => r.Id));
        Assert.Equal(w.Student.StudentCode, pending.Items[0].StudentCode);
        Assert.Empty(rejected.Items);
        Assert.Contains(request.Id, all.Items.Select(r => r.Id)); // no status filter = every status (#329)
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(school2.Items); // only the unit asked for (and below)
    }

    [Fact]
    public async Task Teacher_cannot_see_queue_403()
    {
        var w = await _kit.SetUpAsync("CR12");
        var request = await FileAsync(w);
        var teacher = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests, teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequest(request.Id), teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), teacher, new DecideChangeRequestRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), teacher, new DecideChangeRequestRequest("x"))).StatusCode);
    }

    [Fact]
    public async Task School2_counselor_gets_404_for_School1_request()
    {
        var w = await _kit.SetUpAsync("CR13");
        var request = await FileAsync(w);
        var counselor2 = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School2, BuiltInRoles.Counselor, Guid.NewGuid());
        var otherClientAdmin = (await _kit.SetUpAsync("CR13B")).Admin;

        foreach (var who in new[] { counselor2, otherClientAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequest(request.Id), who)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), who, new DecideChangeRequestRequest(null))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestReject(request.Id), who, new DecideChangeRequestRequest("x"))).StatusCode);
        }

        var list = await _kit.ReadAsync<PagedResult<StudentChangeRequestDto>>(await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequests + "?status=Pending", counselor2));
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Deleting_or_moving_the_student_handles_the_pending_request()
    {
        var w = await _kit.SetUpAsync("CR14");
        var request = await FileAsync(w);
        var counselor2 = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School2, BuiltInRoles.Counselor, Guid.NewGuid());
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<StudentChangeRequest>().SingleAsync(r => r.Id == request.Id);
            stored.MoveHome(w.Tree.School2); // what a home move does to a pending request
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentChangeRequest(request.Id), counselor2)).StatusCode);

        var deleted = await _kit.SendAsync(HttpMethod.Delete, Routes.Student(w.Student.Id), w.Admin);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var check = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(ChangeRequestStatus.Cancelled, (await check.Set<StudentChangeRequest>().SingleAsync(r => r.Id == request.Id)).Status);
    }

    [Fact]
    public async Task Approved_photo_and_date_changes_are_applied_in_normal_form()
    {
        var w = await _kit.SetUpAsync("CR15");
        var request = await FileAsync(w, new CreateChangeRequestRequest(
            Changes((StudentFields.DateOfBirth, "2013-02-03"), (StudentFields.Gender, "Female")), null));

        var response = await _kit.SendAsync(HttpMethod.Post, Routes.StudentChangeRequestApprove(request.Id), w.Admin, new DecideChangeRequestRequest(null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await _kit.ReloadAsync(w.Student.Id);
        Assert.Equal((new DateOnly(2013, 2, 3), "Female"), (stored.DateOfBirth, stored.Gender));
    }
}
