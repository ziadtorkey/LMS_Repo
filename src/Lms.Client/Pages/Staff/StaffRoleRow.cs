using Lms.Shared.People;

namespace Lms.Client.Pages.Staff;

/// <summary>One editable role row of the create-staff form (role at a unit, optional validity window).</summary>
public sealed class StaffRoleRow
{
    public Guid? RoleId { get; set; }
    public Guid? UnitId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    /// <summary>Resource key of what is wrong with this row after <see cref="ValidateAll"/>; null when it is fine.</summary>
    public string? Problem { get; private set; }

    /// <summary>A row the user never touched; it is ignored.</summary>
    public bool IsEmpty => RoleId is null && UnitId is null && ValidFrom is null && ValidTo is null;

    private string? FindProblem() =>
        IsEmpty ? null
        : RoleId is null || UnitId is null ? "Staff_RoleIncomplete"
        : ValidFrom is { } f && ValidTo is { } t && t < f ? "Assignments_DateOrder"
        : null;

    /// <summary>Validates every row that is not empty (a partly filled row is flagged, never dropped); true when all are fine.</summary>
    public static bool ValidateAll(IEnumerable<StaffRoleRow> rows)
    {
        var ok = true;
        foreach (var row in rows)
        {
            row.Problem = row.FindProblem();
            ok &= row.Problem is null;
        }
        return ok;
    }

    public StaffRoleInput ToInput() => new(RoleId!.Value, UnitId!.Value,
        ValidFrom is { } f ? DateOnly.FromDateTime(f) : null,
        ValidTo is { } t ? DateOnly.FromDateTime(t) : null);
}
