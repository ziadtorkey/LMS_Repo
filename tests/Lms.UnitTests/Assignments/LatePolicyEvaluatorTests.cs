using Lms.Infrastructure.Assignments;
using Lms.Shared.Assignments;

namespace Lms.UnitTests.Assignments;

public sealed class LatePolicyEvaluatorTests
{
    private static readonly DateTime Due = new(2041, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(LatePolicies.Allow)]
    [InlineData(LatePolicies.Penalty)]
    [InlineData(LatePolicies.Block)]
    public void On_or_before_the_due_time_is_never_late(string policy)
    {
        Assert.Equal(new LateOutcome(false, false), LatePolicyEvaluator.Evaluate(policy, Due, Due.AddHours(-1)));
        Assert.Equal(new LateOutcome(false, false), LatePolicyEvaluator.Evaluate(policy, Due, Due));
    }

    [Theory]
    [InlineData(LatePolicies.Allow, false)]
    [InlineData(LatePolicies.Penalty, false)]
    [InlineData(LatePolicies.Block, true)]
    public void After_the_due_time_is_late_and_block_refuses(string policy, bool refused) =>
        Assert.Equal(new LateOutcome(true, refused), LatePolicyEvaluator.Evaluate(policy, Due, Due.AddSeconds(1)));

    [Fact]
    public void No_due_date_is_never_late() =>
        Assert.Equal(new LateOutcome(false, false), LatePolicyEvaluator.Evaluate(LatePolicies.Block, null, DateTime.MaxValue));
}
