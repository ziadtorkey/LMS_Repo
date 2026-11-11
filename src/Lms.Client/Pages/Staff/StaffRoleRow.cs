using Lms.Shared.People;

namespace Lms.Client.Pages.Staff;

/// <summary>One editable role row of the create-staff form (role at a unit, optional validity window).</summary>
public sealed class StaffRoleRow
{
    public Guid? RoleId { get; set; }
    public Guid? UnitId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public bool IsComplete => RoleId is not null && UnitId is not null && (ValidFrom is null || ValidTo is null || ValidTo >= ValidFrom);

    public StaffRoleInput ToInput() => new(RoleId!.Value, UnitId!.Value,
        ValidFrom is { } f ? DateOnly.FromDateTime(f) : null,
        ValidTo is { } t ? DateOnly.FromDateTime(t) : null);
}
