using System.Net;
using Lms.Api.Features.Students.SelfService;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.People;
using Xunit;
using static Lms.IntegrationTests.Students.SelfServiceKit;

namespace Lms.IntegrationTests.Students;

/// <summary>M3 Task 22 (#230), D27: a student edits non-critical fields directly; critical ones need a request.</summary>
[Collection(ApiCollection.Name)]
public sealed class SelfEditTests
{
    private readonly SelfServiceKit _kit;

    public SelfEditTests(LmsApiFactory factory) => _kit = new SelfServiceKit(factory, factory.WithWebHostBuilder(_ => { }));

    [Fact]
    public void Critical_setting_moves_fields_between_editable_and_requestable()
    {
        var (editable, requestable) = StudentFieldRules.Classify(StudentFields.DefaultCritical.ToList());
        Assert.Contains(StudentFields.Mobile, editable);
        Assert.Contains(StudentFields.NicknameEn, editable);
        Assert.Contains(StudentFields.Photo, editable);
        Assert.Contains(StudentFields.NationalId, requestable);
        Assert.DoesNotContain(StudentFields.NationalId, editable);

        var (editable2, requestable2) = StudentFieldRules.Classify([StudentFields.Mobile]);
        Assert.Contains(StudentFields.NationalId, editable2);
        Assert.Equal([StudentFields.Mobile], requestable2);

        foreach (var set in new[] { editable, requestable, editable2, requestable2 })
        {
            Assert.DoesNotContain(StudentFields.StudentCode, set);
            Assert.DoesNotContain(StudentFields.Email, set);
        }
    }

    [Fact]
    public async Task Student_reads_the_own_profile_with_editable_and_requestable_fields()
    {
        var w = await _kit.SetUpAsync("SE1");

        var response = await _kit.SendAsync(HttpMethod.Get, Routes.MyStudentProfile, w.Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await _kit.ReadAsync<MyStudentProfileDto>(response);
        Assert.Equal(w.Student.Id, dto.Student.Summary.Id);
        Assert.Contains(StudentFields.Mobile, dto.EditableFields);
        Assert.Contains(StudentFields.NationalId, dto.RequestableFields);
        Assert.DoesNotContain(StudentFields.StudentCode, dto.EditableFields.Concat(dto.RequestableFields));
    }

    [Fact]
    public async Task A_caller_without_self_edit_permission_gets_403()
    {
        var w = await _kit.SetUpAsync("SE2");

        // the admin holds no Students.SelfEdit at all
        Assert.Equal(HttpStatusCode.Forbidden, (await _kit.SendAsync(HttpMethod.Get, Routes.MyStudentProfile, w.Admin)).StatusCode);
    }

    [Fact]
    public async Task Student_updates_nickname_and_mobile_directly()
    {
        var w = await _kit.SetUpAsync("SE3");

        var response = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me,
            new UpdateMyStudentProfileRequest(Changes((StudentFields.NicknameEn, "Sam"), (StudentFields.Mobile, "+201012345678"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await _kit.ReloadAsync(w.Student.Id);
        Assert.Equal("Sam", stored.NicknameEn);
        Assert.Equal("+201012345678", stored.Mobile);
        Assert.Equal(w.Student.NameEn, stored.NameEn); // untouched
    }

    [Theory]
    [InlineData(StudentFields.NationalId)]
    [InlineData(StudentFields.NameEn)]
    [InlineData(StudentFields.StudentCode)]
    [InlineData(StudentFields.Email)]
    public async Task Student_editing_a_critical_or_fixed_field_directly_is_400_critical_field(string field)
    {
        var w = await _kit.SetUpAsync("SE4" + field[..3]);

        var response = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me,
            new UpdateMyStudentProfileRequest(Changes((StudentFields.NicknameEn, "Sam"), (field, "X123456789"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.critical_field, await _kit.CodeAsync(response));
        Assert.Null((await _kit.ReloadAsync(w.Student.Id)).NicknameEn); // all-or-nothing
    }

    [Fact]
    public async Task Invalid_values_are_refused_like_staff_edits()
    {
        var w = await _kit.SetUpAsync("SE5");

        var mobile = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me,
            new UpdateMyStudentProfileRequest(Changes((StudentFields.Mobile, "abc"))));
        var nick = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me,
            new UpdateMyStudentProfileRequest(Changes((StudentFields.NicknameEn, new string('x', 201)))));
        var photo = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me,
            new UpdateMyStudentProfileRequest(Changes((StudentFields.Photo, Guid.NewGuid().ToString()))));
        var empty = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me, new UpdateMyStudentProfileRequest(Changes()));

        Assert.All(new[] { mobile, nick, photo, empty }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task Admin_moving_NationalId_out_of_critical_setting_lets_student_edit_it()
    {
        var w = await _kit.SetUpAsync("SE6");
        var body = new UpdateMyStudentProfileRequest(Changes((StudentFields.NationalId, "N" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant())));
        Assert.Equal(HttpStatusCode.BadRequest, (await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me, body)).StatusCode);

        await _kit.SetCriticalAsync(w.Tree, StudentFields.NameAr, StudentFields.NameEn);

        var ok = await _kit.SendAsync(HttpMethod.Put, Routes.MyStudentProfile, w.Me, body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotNull((await _kit.ReloadAsync(w.Student.Id)).NationalId);
        // and the other way round: it can no longer be requested
        var request = await _kit.SendAsync(HttpMethod.Post, Routes.MyChangeRequests, w.Me,
            new CreateChangeRequestRequest(Changes((StudentFields.NationalId, "N0000000001")), null));
        Assert.Equal(ProblemCodes.critical_field, await _kit.CodeAsync(request));
    }
}
