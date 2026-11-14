using Lms.Domain.Organizations;

namespace Lms.Domain.Grading;

/// <summary>A scale with its bands, as a unit sees it (its own or an ancestor's).</summary>
public sealed record VisibleScale(GradingScale Scale, IReadOnlyList<GradingScaleBand> Bands);

/// <summary>A weight template with its items, as a unit sees it.</summary>
public sealed record VisibleWeightTemplate(WeightTemplate Template, IReadOnlyList<WeightTemplateItem> Items);

/// <summary>
/// The scales and weight templates a unit sees: its own and those of its ancestors in the same client, read only above the unit (M5 plan bypass
/// site 1). Only names, kinds, bands and weights are read across the unit boundary.
/// </summary>
public interface IScaleResolver
{
    Task<IReadOnlyList<VisibleScale>> ScalesAsync(OrgUnit unit, CancellationToken cancellationToken);

    Task<VisibleScale?> FindScaleAsync(OrgUnit unit, Guid scaleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VisibleWeightTemplate>> TemplatesAsync(OrgUnit unit, CancellationToken cancellationToken);

    Task<VisibleWeightTemplate?> FindTemplateAsync(OrgUnit unit, Guid templateId, CancellationToken cancellationToken);
}

/// <summary>The preset catalog the reconciler seeds from. The default is <see cref="SaudiPresetSeed"/>; tests bump versions through a fake.</summary>
public interface IScalePresetCatalog
{
    IReadOnlyList<SeedScale> Presets { get; }
}

/// <summary>Seeds a client's plain default scale and its preset templates, and keeps the templates at the catalog's version (M5 bypass site 9).</summary>
public interface IPresetReconciler
{
    /// <summary>Adds what is missing for one client at its root unit; never touches an edited copy. Returns the number of rows added or updated.</summary>
    Task<int> ReconcileAsync(Guid organizationId, CancellationToken cancellationToken);
}
