namespace Lms.Infrastructure.Configuration;

public interface IPolicyManager
{
    /// <summary>Switches the policy on at the unit (idempotent; brings back a previously removed policy).</summary>
    Task EnableAsync(Guid orgUnitId, string policyKey, CancellationToken cancellationToken = default);

    /// <summary>Switches the policy off at the unit (soft delete; no-op when it is not on).</summary>
    Task DisableAsync(Guid orgUnitId, string policyKey, CancellationToken cancellationToken = default);
}
