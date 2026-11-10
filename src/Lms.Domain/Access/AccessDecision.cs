namespace Lms.Domain.Access;

public sealed record AccessDecision(bool Allowed, AccessDenialReason Reason, Guid? BlockingOrgUnitId = null)
{
    public static AccessDecision Allow { get; } = new(true, AccessDenialReason.None);

    public static AccessDecision Deny(AccessDenialReason reason, Guid? blockingOrgUnitId = null) =>
        new(false, reason, blockingOrgUnitId);
}