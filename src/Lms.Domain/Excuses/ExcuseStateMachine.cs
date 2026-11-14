namespace Lms.Domain.Excuses;

/// <summary>
/// The statuses of an absence excuse. The domain does not reference the contract project, so the values are repeated here and pinned to
/// <c>Lms.Shared.Excuses.ExcuseStatuses</c> by a unit test.
/// </summary>
public static class ExcuseStatus
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Escalated = "Escalated";
    public const string Withdrawn = "Withdrawn";
}

/// <summary>What someone does to an excuse.</summary>
public enum ExcuseAction
{
    Approve,
    Reject,
    Escalate,
    Withdraw,
}

/// <summary>An action that the state machine refuses (409 <c>excuse_not_pending</c>).</summary>
public sealed class ExcuseTransitionException(string from, ExcuseAction action)
    : InvalidOperationException($"An excuse that is {from} cannot be {action}.")
{
    public string From { get; } = from;

    public ExcuseAction Action { get; } = action;
}

/// <summary>
/// The state machine of an excuse (FR-EXC-03, D61): <c>Submitted</c> → <c>Approved</c> | <c>Rejected</c> | <c>Escalated</c> (→ <c>Approved</c> | <c>Rejected</c>), or
/// <c>Withdrawn</c> by the submitter while <c>Submitted</c>. Approved, Rejected and Withdrawn are final; an escalated excuse cannot be escalated again or withdrawn.
/// </summary>
public static class ExcuseStateMachine
{
    /// <summary>True for the statuses a reviewer can still decide.</summary>
    public static bool IsPending(string status) => status is ExcuseStatus.Submitted or ExcuseStatus.Escalated;

    public static bool CanApply(string from, ExcuseAction action) => action switch
    {
        ExcuseAction.Approve or ExcuseAction.Reject => IsPending(from),
        ExcuseAction.Escalate or ExcuseAction.Withdraw => from == ExcuseStatus.Submitted,
        _ => false,
    };

    /// <summary>The status after the action, or <see cref="ExcuseTransitionException"/> when it is not allowed from <paramref name="from"/>.</summary>
    public static string Apply(string from, ExcuseAction action)
    {
        if (!CanApply(from, action))
        {
            throw new ExcuseTransitionException(from, action);
        }

        return action switch
        {
            ExcuseAction.Approve => ExcuseStatus.Approved,
            ExcuseAction.Reject => ExcuseStatus.Rejected,
            ExcuseAction.Escalate => ExcuseStatus.Escalated,
            _ => ExcuseStatus.Withdrawn,
        };
    }

    /// <summary>The reviewer of an excuse is never the person who submitted it (409 <c>excuse_reviewer_is_submitter</c>).</summary>
    public static bool MayReview(Guid reviewerUserId, Guid submittedByUserId) => reviewerUserId != submittedByUserId;
}
