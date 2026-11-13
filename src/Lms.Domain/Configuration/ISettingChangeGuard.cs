using Lms.Domain.Organizations;

namespace Lms.Domain.Configuration;

/// <summary>
/// A rule a setting must pass before it is changed at a unit (the enable hook): for example, turning on <c>Features.VirtualClasses</c> needs
/// a configured and healthy meeting provider. The settings route asks every guard registered for the setting name, after the value itself
/// has been validated and before it is written; the first refusal answers 409 with the returned problem code and nothing is stored.
/// A guard for a name that is never changed through the route is simply never called.
/// </summary>
public interface ISettingChangeGuard
{
    /// <summary>The setting this guard is about, for example <c>Features.VirtualClasses</c>.</summary>
    string SettingName { get; }

    /// <param name="unit">The unit the value is set at.</param>
    /// <param name="valueJson">The JSON value about to be stored, already validated against the catalog.</param>
    /// <returns>A <c>ProblemCodes</c> value when the change is refused; null when it may go ahead.</returns>
    Task<string?> RefuseAsync(OrgUnit unit, string valueJson, CancellationToken ct);
}
