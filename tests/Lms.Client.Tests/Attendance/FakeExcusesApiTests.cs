using Lms.Client.Services.Api;
using Lms.Client.Services.Excuses;
using Lms.Shared.Excuses;

namespace Lms.Client.Tests.Attendance;

public sealed class FakeExcusesApiTests
{
    private static readonly SubmitExcuseRequest Request = new(FakeExcusesApi.Student, "Illness", "Fever", new DateOnly(2026, 11, 18), null, null, null);

    [Fact]
    public async Task Fake_excuse_state_machine()
    {
        var api = new FakeExcusesApi();
        var excuse = await api.SubmitAsync(Request);
        Assert.Equal(ExcuseStatuses.Submitted, excuse.Status);

        api.CurrentUser = FakeExcusesApi.Counselor; // a reviewer who is not the submitter
        Assert.Equal("excuse_comment_required", (await Assert.ThrowsAsync<ApiProblemException>(() => api.RejectAsync(excuse.Id, new RejectExcuseRequest(" ")))).Code);

        var escalated = await api.EscalateAsync(excuse.Id, new EscalateExcuseRequest(Guid.NewGuid(), "Needs the manager"));
        Assert.Equal(ExcuseStatuses.Escalated, escalated.Status);
        var approved = await api.ApproveAsync(excuse.Id, new ApproveExcuseRequest("OK")); // an escalated excuse can still be decided
        Assert.Equal(ExcuseStatuses.Approved, approved.Status);
        Assert.False(approved.CanWithdraw);

        Assert.Equal("excuse_not_pending", (await Assert.ThrowsAsync<ApiProblemException>(() => api.ApproveAsync(excuse.Id, new ApproveExcuseRequest(null)))).Code);
    }

    [Fact]
    public async Task Only_the_submitter_withdraws_and_only_while_submitted()
    {
        var api = new FakeExcusesApi();
        var excuse = await api.SubmitAsync(Request);

        api.CurrentUser = FakeExcusesApi.Counselor;
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiProblemException>(() => api.WithdrawAsync(excuse.Id))).Status);

        api.CurrentUser = FakeExcusesApi.Guardian;
        Assert.Equal(ExcuseStatuses.Withdrawn, (await api.WithdrawAsync(excuse.Id)).Status);
        Assert.Equal("excuse_not_pending", (await Assert.ThrowsAsync<ApiProblemException>(() => api.WithdrawAsync(excuse.Id))).Code);
    }

    [Fact]
    public async Task The_reviewer_cannot_be_the_submitter_and_a_student_cannot_submit_under_GuardiansOnly()
    {
        var api = new FakeExcusesApi();
        var excuse = await api.SubmitAsync(Request);
        Assert.Equal("excuse_reviewer_is_submitter", (await Assert.ThrowsAsync<ApiProblemException>(() => api.ApproveAsync(excuse.Id, new ApproveExcuseRequest(null)))).Code);

        var student = new FakeExcusesApi { CurrentUserIsStudent = true };
        Assert.False((await student.GetPolicyAsync(FakeExcusesApi.Student)).CanSubmit);
        Assert.Equal("excuse_submitter_not_allowed", (await Assert.ThrowsAsync<ApiProblemException>(() => student.SubmitAsync(Request))).Code);
    }
}
