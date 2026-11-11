using Lms.Shared.People;

namespace Lms.Client.Components.People;

/// <summary>One guardian row of the create-student form: a new guardian's data, or a link to a registered guardian.</summary>
public sealed class GuardianRow
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public string NationalId { get; set; } = "";
    public string Relation { get; set; } = GuardianRelations.Father;
    public bool IsPrimary { get; set; }

    /// <summary>A registered guardian found by national id or mobile; offered to the user, who may link it.</summary>
    public GuardianMatchDto? Match { get; set; }

    /// <summary>Set once the user chose to link the matched guardian; the typed data is then ignored.</summary>
    public Guid? LinkedGuardianId { get; set; }

    /// <summary>Resource key of what is wrong with this row after <see cref="ValidateAll"/>; null when it is fine.</summary>
    public string? Problem { get; private set; }

    public bool IsEmpty => LinkedGuardianId is null && NameAr.Length == 0 && NameEn.Length == 0 && Mobile.Length == 0
        && Email.Length == 0 && NationalId.Length == 0;

    private string? FindProblem() =>
        IsEmpty || LinkedGuardianId is not null ? null
        : string.IsNullOrWhiteSpace(NameAr) || string.IsNullOrWhiteSpace(NameEn) || string.IsNullOrWhiteSpace(Mobile) ? "Guardian_Incomplete"
        : null;

    /// <summary>Validates every row that is not empty (a partly filled row is flagged, never dropped); true when all are fine.</summary>
    public static bool ValidateAll(IEnumerable<GuardianRow> rows)
    {
        var ok = true;
        foreach (var row in rows)
        {
            row.Problem = row.FindProblem();
            ok &= row.Problem is null;
        }
        return ok;
    }

    /// <summary>Makes <paramref name="chosen"/> the only primary guardian.</summary>
    public static void SetPrimary(IEnumerable<GuardianRow> rows, GuardianRow chosen)
    {
        foreach (var row in rows) row.IsPrimary = ReferenceEquals(row, chosen);
    }

    public GuardianLinkInput ToInput() => LinkedGuardianId is { } id
        ? new GuardianLinkInput(id, null, Relation, IsPrimary)
        : new GuardianLinkInput(null, new GuardianDataInput(NameAr.Trim(), NameEn.Trim(), Mobile.Trim(),
            string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(), string.IsNullOrWhiteSpace(NationalId) ? null : NationalId.Trim()), Relation, IsPrimary);
}
