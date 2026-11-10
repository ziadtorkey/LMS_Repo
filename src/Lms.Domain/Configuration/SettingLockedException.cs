namespace Lms.Domain.Configuration;

/// <summary>Thrown when a setting is written below an ancestor org unit that locked it.</summary>
public sealed class SettingLockedException(Guid lockingOrgUnitId)
    : InvalidOperationException("The setting is locked by a parent organization unit.")
{
    public Guid LockingOrgUnitId { get; } = lockingOrgUnitId;
}
