namespace Lms.Infrastructure.Assignments;

/// <summary>What a hand-in at a given instant means under the assignment late policy (FR-ASG-01).</summary>
public readonly record struct LateOutcome(bool IsLate, bool Refused);

/// <summary>Decides late and refused on the server clock only. Allow flags late, Penalty flags late, Block refuses.</summary>
public static class LatePolicyEvaluator
{
    public static LateOutcome Evaluate(string latePolicy, DateTime? dueAtUtc, DateTime nowUtc)
    {
        if (dueAtUtc is not { } due || nowUtc <= due)
        {
            return new LateOutcome(false, false);
        }

        return new LateOutcome(true, latePolicy == "Block");
    }
}
