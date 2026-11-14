using Lms.Shared.Configuration;

namespace Lms.Shared.Me;

/// <summary>
/// GET <c>/api/me/capabilities</c> (authenticated, no permission; M6 L17): the effective feature flags for the caller's active context, keyed by flag name
/// (<c>Features.Messaging</c> first). The Messages nav entry hides when its flag is off; the server still answers 403 <c>feature_disabled</c>.
/// </summary>
public sealed record CapabilitiesDto(IReadOnlyDictionary<string, bool> Features)
{
    /// <summary>A flag that is not listed is off.</summary>
    public bool Has(SettingKey<bool> flag) => Features.TryGetValue(flag.Name, out var on) && on;
}
