using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Branches;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Templates;

public static partial class TemplateEndpoints
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();

    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Templates, ListAsync).RequireAuthorization().RequirePermission(Permissions.Templates.View);
        app.MapGet(Routes.TemplatePattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Templates.View);
        app.MapPost(Routes.Templates, SaveAsync).RequireAuthorization().RequirePermission(Permissions.Templates.Create);
        app.MapDelete(Routes.TemplatePattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Templates.Delete);
        app.MapPost(Routes.TemplateApplyPattern, ApplyAsync).RequireAuthorization().RequirePermission(Permissions.Templates.Apply);
        return app;
    }

    private static async Task<IResult> ListAsync(LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var templates = await Visible(db, context).OrderBy(t => t.NameEn).ThenBy(t => t.Id).ToListAsync(ct);
        return Results.Ok(await TemplateMapper.ToSummariesAsync(db, templates, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var template = await Visible(db, context).SingleOrDefaultAsync(t => t.Id == id, ct);
        return template is null ? ApiProblems.NotFound() : Results.Ok(await TemplateMapper.ToDetailAsync(db, template, ct));
    }

    private static async Task<IResult> SaveAsync(
        SaveTemplateRequest request, HttpContext http, LmsDbContext db, IBranchSnapshotter snapshotter, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var access = await http.CheckUnitAsync(Permissions.Templates.Create, request.SourceOrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        BranchSnapshot snapshot;
        try
        {
            snapshot = await snapshotter.CaptureAsync(access.Unit!, request.IncludePolicies, request.IncludeSettings, ct);
        }
        catch (BranchTooLargeException)
        {
            return ApiProblems.BadRequest(ProblemCodes.branch_too_large);
        }

        var nodes = snapshot.Nodes.Select(n => n with
        {
            NameAr = TemplatePlaceholders.Parameterize(n.NameAr, request.Parameterize),
            NameEn = TemplatePlaceholders.Parameterize(n.NameEn, request.Parameterize),
            Code = TemplatePlaceholders.Parameterize(n.Code, request.Parameterize),
        }).ToList();
        var placeholders = nodes.SelectMany(n => new[] { n.NameAr, n.NameEn, n.Code })
            .SelectMany(TemplatePlaceholders.Find).ToHashSet(StringComparer.Ordinal);

        var template = new OrgTemplate(
            access.Unit!, request.NameAr.Trim(), request.NameEn.Trim(), Enum.Parse<TemplateVisibility>(request.Visibility),
            JsonSerializer.Serialize(new BranchSnapshot(nodes), Json), nodes.Count, placeholders);
        db.Set<OrgTemplate>().Add(template);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Template(template.Id), await TemplateMapper.ToDetailAsync(db, template, ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var template = await Visible(db, context).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Templates.Delete, template.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        db.Set<OrgTemplate>().Remove(template);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ApplyAsync(
        Guid id, ApplyTemplateRequest request, HttpContext http, LmsDbContext db, ICurrentContext context,
        IBranchMaterializer materializer, CancellationToken ct)
    {
        var template = await Visible(db, context).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return ApiProblems.NotFound();
        }

        var apply = await http.CheckUnitAsync(Permissions.Templates.Apply, request.TargetParentId, ct);
        if (apply.Problem is not null)
        {
            return apply.Problem;
        }

        var create = await http.CheckUnitAsync(Permissions.OrgUnits.Create, request.TargetParentId, ct);
        if (create.Problem is not null)
        {
            return create.Problem;
        }

        var values = request.Placeholders ?? new Dictionary<string, string>();
        var required = template.Placeholders.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (required.Any(name => !values.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v)))
        {
            return ApiProblems.BadRequest(ProblemCodes.placeholder_missing);
        }

        if (values.Keys.Any(name => !required.Contains(name)))
        {
            return ApiProblems.BadRequest(ProblemCodes.placeholder_unknown);
        }

        var snapshot = JsonSerializer.Deserialize<BranchSnapshot>(template.SnapshotJson, Json)!;
        var trimmed = values.ToDictionary(v => v.Key, v => v.Value.Trim(), StringComparer.Ordinal);
        var nodes = snapshot.Nodes.Select(n => n with
        {
            NameAr = TemplatePlaceholders.Apply(n.NameAr, trimmed),
            NameEn = TemplatePlaceholders.Apply(n.NameEn, trimmed),
            Code = TemplatePlaceholders.Apply(n.Code, trimmed),
        }).ToList();
        if (nodes.Any(n => !CodePattern().IsMatch(n.Code) || n.NameAr.Length > 200 || n.NameEn.Length > 200))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A unit code or name produced by the placeholders is invalid.");
        }

        try
        {
            var result = await materializer.MaterializeAsync(
                new BranchSnapshot(nodes), apply.Unit!, name => name, code => code, ct);
            return Results.Created(Routes.OrgUnit(result.NewRoot.Id),
                new ApplyTemplateResultDto(result.NewRoot.Id, result.UnitsCreated, result.SkippedLockedSettings));
        }
        catch (BranchCodeConflictException conflict)
        {
            return ApiProblems.Conflict(ProblemCodes.code_taken, extra: conflict.Codes);
        }
        catch (BranchInvalidException)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
        }
    }

    /// <summary>
    /// Shared templates are visible client-wide; Private ones to callers whose scope contains the owner unit or lies
    /// inside it (the client itself is the tenant filter). A platform admin sees every template.
    /// </summary>
    private static IQueryable<OrgTemplate> Visible(LmsDbContext db, ICurrentContext context)
    {
        var all = db.Set<OrgTemplate>().AsQueryable();
        if (context.IsPlatformAdmin)
        {
            return all;
        }

        if (context.ScopePath is not { Length: > 0 } scope)
        {
            return all.Where(_ => false);
        }

        var ancestors = OrgUnitPath.AncestorsAndSelf(scope);
        return all.Where(t =>
            t.Visibility == TemplateVisibility.Shared
            || t.OrgUnitPath.StartsWith(scope)
            || ancestors.Contains(t.OrgUnitPath));
    }

    private static IResult? Validate(SaveTemplateRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NameAr) || request.NameAr.Trim().Length > 200)
        {
            errors[nameof(request.NameAr)] = ["name_invalid"];
        }

        if (string.IsNullOrWhiteSpace(request.NameEn) || request.NameEn.Trim().Length > 200)
        {
            errors[nameof(request.NameEn)] = ["name_invalid"];
        }

        if (!TemplateVisibilities.All.Contains(request.Visibility))
        {
            errors[nameof(request.Visibility)] = ["visibility_invalid"];
        }

        if (request.Parameterize is { } map
            && map.Any(m => string.IsNullOrEmpty(m.Key) || !TemplatePlaceholders.IsToken(m.Value)))
        {
            errors[nameof(request.Parameterize)] = ["placeholder_invalid"];
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
    }
}
