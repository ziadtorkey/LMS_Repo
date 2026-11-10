using Lms.Api.Infrastructure;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Configuration;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Policies;

public static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.PoliciesCatalog, GetCatalog).RequirePermission(Permissions.Policies.View);
        app.MapGet(Routes.OrgUnitPoliciesPattern, GetEffectiveAsync).RequirePermission(Permissions.Policies.View);
        app.MapPut(Routes.OrgUnitPolicyPattern, EnableAsync).RequirePermission(Permissions.Policies.Update);
        app.MapDelete(Routes.OrgUnitPolicyPattern, DisableAsync).RequirePermission(Permissions.Policies.Update);
        return app;
    }

    private static IResult GetCatalog() =>
        Results.Ok(PolicyCatalog.BlockedPermissions
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new PolicyDefinitionDto(x.Key, x.Value))
            .ToList());

    private static async Task<IResult> GetEffectiveAsync(
        Guid id, HttpContext http, IEffectivePolicyReader reader, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Policies.View, id, ct);
        return access.Problem ?? Results.Ok(await reader.ReadAsync(access.Unit!, ct));
    }

    private static async Task<IResult> EnableAsync(
        Guid id, string key, HttpContext http, IEffectivePolicyReader reader, IPolicyManager policies,
        INotificationPublisher notifications, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Policies.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (!PolicyCatalog.BlockedPermissions.ContainsKey(key))
        {
            return ApiProblems.NotFound();
        }

        var before = (await reader.ReadAsync(unit, ct)).Single(x => x.Key == key);
        if (before.State != PolicyStates.SetHere)
        {
            await policies.EnableAsync(unit.Id, key, ct);
            await NotifyAsync(notifications, unit, key, state: "On", ct);
        }

        return Results.Ok((await reader.ReadAsync(unit, ct)).Single(x => x.Key == key));
    }

    private static async Task<IResult> DisableAsync(
        Guid id, string key, HttpContext http, IEffectivePolicyReader reader, IPolicyManager policies,
        INotificationPublisher notifications, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Policies.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (!PolicyCatalog.BlockedPermissions.ContainsKey(key))
        {
            return ApiProblems.NotFound();
        }

        var state = (await reader.ReadAsync(unit, ct)).Single(x => x.Key == key);
        switch (state.State)
        {
            case PolicyStates.Off:
                return ApiProblems.NotFound();
            case PolicyStates.Inherited:
                // Children can add policies, never remove one set above them.
                return ApiProblems.Conflict(ProblemCodes.policy_inherited, extra: new
                {
                    sourceOrgUnitId = state.SourceOrgUnitId,
                    sourceNameAr = state.SourceNameAr,
                    sourceNameEn = state.SourceNameEn,
                });
        }

        await policies.DisableAsync(unit.Id, key, ct);
        await NotifyAsync(notifications, unit, key, state: "Off", ct);
        return Results.NoContent();
    }

    /// <summary>Tells the admins and managers at and below the unit (not the actor) that a policy changed.</summary>
    private static Task NotifyAsync(
        INotificationPublisher notifications, Lms.Domain.Organizations.OrgUnit unit, string key, string state, CancellationToken ct) =>
        notifications.PublishAsync(new NotificationRequest(
            NotificationEvents.OrganizationPolicyChanged,
            unit.OrganizationId,
            [],
            new Dictionary<string, string>
            {
                ["PolicyKey"] = key,
                ["OrgUnitNameAr"] = unit.NameAr,
                ["OrgUnitNameEn"] = unit.NameEn,
                ["State"] = state,
            },
            OrgUnitPath: unit.Path), ct);
}
