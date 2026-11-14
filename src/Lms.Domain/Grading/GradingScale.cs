using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Grading;

/// <summary>
/// A grading scale owned by an org unit (D58, D59, L7). Units see the scales of their ancestors, read only. Provisioning gives a client one plain
/// default scale and the Saudi presets as templates (<see cref="IsTemplate"/>, never the default, <see cref="Provisional"/> until confirmed).
/// <see cref="SeedKey"/> and <see cref="SeedVersion"/> tie a template and its copies to the seed; a copy that nobody edited
/// (<see cref="Edited"/> false) can move to a newer seed version, an edited one never does.
/// </summary>
[Audited]
public sealed class GradingScale : UnitOwnedEntity, ILiveKeyed
{
    private GradingScale()
    {
        NameAr = NameEn = Kind = string.Empty;
    }

    private GradingScale(OrgUnit unit, string nameAr, string nameEn, string kind)
    {
        PlaceIn(unit);
        NameAr = nameAr;
        NameEn = nameEn;
        Kind = kind;
    }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string Kind { get; private set; }
    public bool IsTemplate { get; private set; }
    public bool Provisional { get; private set; }

    /// <summary>True once an admin changed a seeded copy: the seed never touches it again.</summary>
    public bool Edited { get; private set; }

    public string? SeedKey { get; private set; }
    public int? SeedVersion { get; private set; }
    public bool IsDefault { get; private set; }

    /// <summary>The unit id while this is the unit's default scale: a unique index over it gives "one default per unit".</summary>
    public Guid? DefaultKey { get; private set; }

    /// <summary>A preset of the seed (a template, or a provisioning copy): provisional until an admin confirms it.</summary>
    public static GradingScale CreateSeeded(OrgUnit unit, string nameAr, string nameEn, string kind, string seedKey, int seedVersion, bool isTemplate) =>
        new(unit, nameAr, nameEn, kind) { SeedKey = seedKey, SeedVersion = seedVersion, IsTemplate = isTemplate, Provisional = true };

    /// <summary>A scale an admin made, or the plain default of provisioning: confirmed, not seeded.</summary>
    public static GradingScale CreatePlain(OrgUnit unit, string nameAr, string nameEn, string kind, bool isDefault)
    {
        var scale = new GradingScale(unit, nameAr, nameEn, kind);
        if (isDefault)
        {
            scale.MakeDefault();
        }

        return scale;
    }

    /// <summary>A copy of a template at a unit: provisional until confirmed or edited, never a template, never the default yet.</summary>
    public GradingScale CopyTo(OrgUnit unit) =>
        new(unit, NameAr, NameEn, Kind) { SeedKey = SeedKey, SeedVersion = SeedVersion, Provisional = true };

    public void Rename(string nameAr, string nameEn, string kind)
    {
        if (NameAr != nameAr || NameEn != nameEn || Kind != kind)
        {
            NameAr = nameAr;
            NameEn = nameEn;
            Kind = kind;
            MarkEdited();
        }
    }

    /// <summary>A change by an admin: a seeded copy is the admin's own now (the seed leaves it alone) and no longer provisional.</summary>
    public void MarkEdited()
    {
        if (SeedKey is not null)
        {
            Edited = true;
        }

        Provisional = false;
    }

    public void Confirm() => Provisional = false;

    /// <summary>Marks the provisioning default so the reconciler adds it only once.</summary>
    public void SetPlainSeed(string seedKey) => SeedKey = seedKey;

    /// <summary>Takes the new seed text and version on an unedited copy (the caller replaces the bands); provisional again.</summary>
    public void ApplySeed(string nameAr, string nameEn, string kind, int seedVersion)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        Kind = kind;
        SeedVersion = seedVersion;
        Provisional = true;
    }

    public void MakeDefault()
    {
        IsDefault = true;
        DefaultKey = OrgUnitId;
    }

    public void ClearDefault()
    {
        IsDefault = false;
        DefaultKey = null;
    }

    public void ClearLiveKeys() => DefaultKey = null;
}
