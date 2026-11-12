using Lms.Api.Infrastructure;
using Lms.Domain.Configuration;
using Lms.Infrastructure.Configuration;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;

namespace Lms.Api.Features.Settings;

public static class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.SettingsCatalog, () => Results.Ok(SettingCatalog.All)).RequirePermission(Permissions.Settings.View);
        app.MapGet(Routes.OrgUnitSettingsPattern, GetEffectiveAsync).RequirePermission(Permissions.Settings.View);
        app.MapPut(Routes.OrgUnitSettingPattern, SetAsync).RequirePermission(Permissions.Settings.Update);
        app.MapDelete(Routes.OrgUnitSettingPattern, RemoveAsync).RequirePermission(Permissions.Settings.Update);
        return app;
    }

    private static async Task<IResult> GetEffectiveAsync(
        Guid id, HttpContext http, IEffectiveSettingsReader reader, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Settings.View, id, ct);
        return access.Problem ?? Results.Ok(await reader.ReadAsync(access.Unit!, ct));
    }

    private static async Task<IResult> SetAsync(
        Guid id, string name, HttpContext http, ISettingsResolver settings, IEffectiveSettingsReader reader,
        SetSettingRequest? body, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Settings.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var definition = SettingCatalog.Find(name);
        if (definition is null)
        {
            return ApiProblems.NotFound();
        }

        var error = SettingValueValidator.Validate(definition, body?.ValueJson ?? string.Empty);
        if (error is not null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["valueJson"] = [error] },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = error == "invalid_code_pattern" ? ProblemCodes.invalid_code_pattern : ProblemCodes.validation_failed,
                });
        }

        try
        {
            await settings.SetJsonAsync(name, id, body!.ValueJson, body.IsLocked, ct);
        }
        catch (SettingLockedException locked)
        {
            return ApiProblems.Conflict(ProblemCodes.setting_locked, extra: new { lockingOrgUnitId = locked.LockingOrgUnitId });
        }

        return Results.Ok((await reader.ReadAsync(access.Unit!, ct)).Single(x => x.Name == name));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, string name, HttpContext http, ISettingsResolver settings, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Settings.Update, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (SettingCatalog.Find(name) is null)
        {
            return ApiProblems.NotFound();
        }

        // Removing the value defined here lets the inherited value (or the catalog default) show through again.
        return await settings.RemoveAsync(name, id, ct) ? Results.NoContent() : ApiProblems.NotFound();
    }
}
