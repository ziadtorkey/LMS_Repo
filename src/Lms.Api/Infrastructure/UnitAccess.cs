using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;

namespace Lms.Api.Infrastructure;

/// <summary>Outcome of a unit-targeted check: the visible unit, or the problem response to return.</summary>
public sealed record UnitAccessResult(OrgUnit? Unit, IResult? Problem);

/// <summary>
/// Access checks for endpoints that name a unit (or the client root). They complement RequirePermission: the
/// permission is evaluated at the unit's path, not the caller's own scope, and units the caller cannot see answer
/// 404 so their existence is never revealed.
/// </summary>
public static class UnitAccess
{
    public static async Task<UnitAccessResult> CheckUnitAsync(
        this HttpContext http, string permission, Guid orgUnitId, CancellationToken ct)
    {
        var unit = await http.RequestServices.GetRequiredService<IOrgTree>().FindVisibleAsync(orgUnitId, ct);
        if (unit is null)
        {
            return new UnitAccessResult(null, ApiProblems.NotFound());
        }

        var decision = await http.RequestServices.GetRequiredService<IAccessEvaluator>()
            .EvaluateAsync(permission, unit.Path, ct);
        return decision.Allowed
            ? new UnitAccessResult(unit, null)
            : new UnitAccessResult(null, AccessProblem.From(decision, http));
    }

    /// <summary>
    /// For client-wide resources (level types, templates, roles...): the permission must hold at the client root.
    /// A platform admin without a client is refused: they act through an endpoint that names the client.
    /// </summary>
    public static async Task<IResult?> CheckClientRootAsync(this HttpContext http, string permission, CancellationToken ct)
    {
        var context = http.RequestServices.GetRequiredService<ICurrentContext>();
        if (context.OrganizationId is not { } organizationId)
        {
            return AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http);
        }

        var root = await http.RequestServices.GetRequiredService<IOrgTree>().GetRootAsync(organizationId, ct);
        var decision = await http.RequestServices.GetRequiredService<IAccessEvaluator>()
            .EvaluateAsync(permission, root.Path, ct);
        return decision.Allowed ? null : AccessProblem.From(decision, http);
    }
}