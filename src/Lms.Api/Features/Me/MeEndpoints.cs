using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Me;

public static class MeEndpoints
{
    public static IServiceCollection AddMe(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserProfileReader, ClaimsUserProfileReader>();
        return services;
    }

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentication only: the caller has no active context yet when choosing one.
        app.MapGet(Routes.Me, GetMeAsync).RequireAuthorization();

        // Needs a valid active context (X-Lms-Context): the middleware already rejected invalid ones with 403.
        app.MapGet(Routes.MyPermissions, GetPermissionsAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetMeAsync(
        ICurrentContext context, LmsDbContext db, IClock clock, IUserProfileReader profiles, CancellationToken ct)
    {
        var userId = context.UserId!.Value;
        var today = DateOnly.FromDateTime(clock.UtcNow);

        var assignments = await db.ValidAssignmentsFor(userId, today)
            .OrderBy(a => a.OrganizationNameEn).ThenBy(a => a.OrgUnitPath).ThenBy(a => a.RoleNameEn)
            .ToListAsync(ct);
        var names = await profiles.GetDisplayNamesAsync(userId, ct);

        return Results.Ok(new MeDto(
            userId, names.Ar, names.En, context.IsPlatformAdmin,
            assignments.Select(a => new UserContextDto(
                a.AssignmentId, a.OrganizationId, a.OrganizationNameAr, a.OrganizationNameEn,
                a.OrgUnitId, a.OrgUnitNameAr, a.OrgUnitNameEn, a.RoleNameAr, a.RoleNameEn)).ToList()));
    }

    private static async Task<IResult> GetPermissionsAsync(
        ICurrentContext context, IAccessEvaluator access, CancellationToken ct)
    {
        if (context.AssignmentId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Context required",
                detail: "Send the active role assignment in the X-Lms-Context header.",
                extensions: new Dictionary<string, object?> { ["code"] = "context_required" });
        }

        // Same enforcement path as every endpoint: a permission is listed exactly when the evaluator would allow it
        // at the context unit (role grant + scope + no blocking ancestor policy).
        var granted = new List<string>();
        foreach (var permission in Permissions.All)
        {
            if ((await access.EvaluateAsync(permission, null, ct)).Allowed)
            {
                granted.Add(permission);
            }
        }

        return Results.Ok(new EffectivePermissionsDto(context.AssignmentId, granted));
    }
}