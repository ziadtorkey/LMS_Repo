namespace Lms.Shared.Enrollment;

/// <summary>
/// Values of the <c>reason</c> extension on a 400 <c>transfer_invalid</c> problem from POST /api/transfers (D43).
/// The client shows a localized message per reason (resource key <c>TransferReject_&lt;reason&gt;</c>).
/// </summary>
public static class TransferRejectReasons
{
    /// <summary>Problem-details extension key that carries the reason.</summary>
    public const string ExtensionKey = "reason";

    /// <summary>The student has no active placement to transfer from.</summary>
    public const string NotPlaced = "not_placed";

    /// <summary>The target class belongs to a different academic year than the current placement.</summary>
    public const string DifferentYear = "different_year";

    /// <summary>The target class is the student's current class.</summary>
    public const string SameClass = "same_class";

    /// <summary>The transfer date is outside the academic year.</summary>
    public const string DateOutsideYear = "date_outside_year";

    /// <summary>The transfer date is before the current placement started.</summary>
    public const string DateBeforePlacement = "date_before_placement";

    /// <summary>Cross-school transfer whose target level type differs from the current one.</summary>
    public const string DifferentLevelType = "different_level_type";

    /// <summary>All reasons, for exhaustive client resource checks.</summary>
    public static readonly IReadOnlyList<string> All =
        [NotPlaced, DifferentYear, SameClass, DateOutsideYear, DateBeforePlacement, DifferentLevelType];
}
