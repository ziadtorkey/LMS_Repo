using Lms.Domain.Excuses;
using Lms.Shared.Excuses;

namespace Lms.UnitTests.Excuses;

public sealed class ExcuseStateMachineTests
{
    [Fact]
    public void Domain_statuses_and_violations_equal_the_contract_values()
    {
        string[] domain = [ExcuseStatus.Submitted, ExcuseStatus.Approved, ExcuseStatus.Rejected, ExcuseStatus.Escalated, ExcuseStatus.Withdrawn];
        Assert.Equal(ExcuseStatuses.All.Order(StringComparer.Ordinal), domain.Order(StringComparer.Ordinal));

        string[] violations = [ExcuseViolation.TooLate, ExcuseViolation.ReasonNotAllowed, ExcuseViolation.AttachmentRequired,
            ExcuseViolation.NothingToExcuse, ExcuseViolation.DatesInvalid];
        Assert.Equal(ExcusePolicyViolations.All.Order(StringComparer.Ordinal), violations.Order(StringComparer.Ordinal));
        Assert.Equal(ExcuseStatuses.Pending.Order(StringComparer.Ordinal),
            domain.Where(ExcuseStateMachine.IsPending).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(ExcuseStatus.Submitted, ExcuseAction.Approve, ExcuseStatus.Approved)]
    [InlineData(ExcuseStatus.Submitted, ExcuseAction.Reject, ExcuseStatus.Rejected)]
    [InlineData(ExcuseStatus.Submitted, ExcuseAction.Escalate, ExcuseStatus.Escalated)]
    [InlineData(ExcuseStatus.Submitted, ExcuseAction.Withdraw, ExcuseStatus.Withdrawn)]
    [InlineData(ExcuseStatus.Escalated, ExcuseAction.Approve, ExcuseStatus.Approved)]
    [InlineData(ExcuseStatus.Escalated, ExcuseAction.Reject, ExcuseStatus.Rejected)]
    public void Allowed_transitions(string from, ExcuseAction action, string expected) =>
        Assert.Equal(expected, ExcuseStateMachine.Apply(from, action));

    [Theory]
    [InlineData(ExcuseStatus.Escalated, ExcuseAction.Escalate)] // no second escalation
    [InlineData(ExcuseStatus.Escalated, ExcuseAction.Withdraw)] // the submitter cannot withdraw once it is with a manager
    [InlineData(ExcuseStatus.Approved, ExcuseAction.Approve)]
    [InlineData(ExcuseStatus.Approved, ExcuseAction.Reject)]
    [InlineData(ExcuseStatus.Rejected, ExcuseAction.Approve)]
    [InlineData(ExcuseStatus.Rejected, ExcuseAction.Escalate)]
    [InlineData(ExcuseStatus.Withdrawn, ExcuseAction.Approve)]
    [InlineData(ExcuseStatus.Withdrawn, ExcuseAction.Withdraw)]
    [InlineData("Unknown", ExcuseAction.Approve)]
    public void Refused_transitions_throw(string from, ExcuseAction action)
    {
        Assert.False(ExcuseStateMachine.CanApply(from, action));
        var refused = Assert.Throws<ExcuseTransitionException>(() => ExcuseStateMachine.Apply(from, action));
        Assert.Equal((from, action), (refused.From, refused.Action));
    }

    [Fact]
    public void Final_statuses_accept_nothing()
    {
        string[] final = [ExcuseStatus.Approved, ExcuseStatus.Rejected, ExcuseStatus.Withdrawn];
        Assert.All(final, status => Assert.All(Enum.GetValues<ExcuseAction>(), action => Assert.False(ExcuseStateMachine.CanApply(status, action))));
    }

    [Fact]
    public void Reviewer_cannot_be_the_submitter()
    {
        var guardian = Guid.NewGuid();
        Assert.False(ExcuseStateMachine.MayReview(guardian, guardian));
        Assert.True(ExcuseStateMachine.MayReview(Guid.NewGuid(), guardian));
    }
}
