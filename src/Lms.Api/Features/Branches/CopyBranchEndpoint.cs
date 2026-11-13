using System.Text.RegularExpressions;
using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Branches;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Branches;

public static partial class CopyBranchEndpoint
{
    private const int MaxCodeLength = 50;

    /// <summary>Reported in <c>SkippedLockedSettings</c> when the caller may not manage policies at the target.</summary>
    public const string PoliciesSkipped = "permission:" + Permissions.Policies.Update;

    /// <summary>Reported in <c>SkippedLockedSettings</c> when the caller may not manage settings at the target.</summary>
    public const string SettingsSkipped = "permission:" + Permissions.Settings.Update;

    /// <summary>Prefix in <c>SkippedLockedSettings</c> of a course allocation not copied because the course is not available at the target.</summary>
    public const string AllocationSkipped = "allocation:";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex CodePattern();

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(Routes.OrgUnitsCopy, HandleAsync).RequireAuthorization().RequirePermission(Permissions.OrgUnits.Create);

    private static async Task<IResult> HandleAsync(
        CopyBranchRequest request, HttpContext http, IBranchSnapshotter snapshotter, IBranchMaterializer materializer,
        CancellationToken ct)
    {
        // Each unit is checked where it sits: the target for creating, the source for reading (404 outside the scope).
        var target = await http.CheckUnitAsync(Permissions.OrgUnits.Create, request.TargetParentId, ct);
        if (target.Problem is not null)
        {
            return target.Problem;
        }

        var source = await http.CheckUnitAsync(Permissions.OrgUnits.View, request.SourceOrgUnitId, ct);
        if (source.Problem is not null)
        {
            return source.Problem;
        }

        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        if (OrgUnitPath.IsWithin(target.Unit!.Path, source.Unit!.Path))
        {
            return ApiProblems.Conflict(ProblemCodes.invalid_move);
        }

        // Copying must not be a way around the permissions of the thing copied: policies and settings are written at the
        // target only when the caller could also set them there by hand; otherwise they are skipped and reported.
        var extras = await BranchExtrasPermission.ResolveAsync(http, request.TargetParentId, request.IncludePolicies, request.IncludeSettings, ct);
        var skippedExtras = extras.Skipped.ToList();
        var courses = request.IncludeCourses;
        if (courses && !await BranchExtrasPermission.MayReadCoursesAsync(http, request.SourceOrgUnitId, ct))
        {
            courses = false;
            skippedExtras.Add(BranchExtrasPermission.CoursesViewSkipped);
        }

        if (courses && !await BranchExtrasPermission.MayCopyCoursesAsync(http, request.TargetParentId, ct))
        {
            courses = false;
            skippedExtras.Add(BranchExtrasPermission.CoursesSkipped);
        }

        try
        {
            var snapshot = await snapshotter.CaptureAsync(source.Unit, extras.Policies, extras.Settings, ct, courses);
            var root = snapshot.Nodes[0] with
            {
                NameAr = string.IsNullOrWhiteSpace(request.NewRootNameAr) ? snapshot.Nodes[0].NameAr : request.NewRootNameAr.Trim(),
                NameEn = string.IsNullOrWhiteSpace(request.NewRootNameEn) ? snapshot.Nodes[0].NameEn : request.NewRootNameEn.Trim(),
            };
            snapshot = new BranchSnapshot([root, .. snapshot.Nodes.Skip(1)]);

            if (snapshot.Nodes.Any(n => !CodePattern().IsMatch(n.Code + request.CodeSuffix)
                || (n.OwnedCourses ?? []).Any(c => !CodePattern().IsMatch(c.Code + request.CodeSuffix))))
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A copied unit or course code would exceed 50 characters.");
            }

            var result = await materializer.MaterializeAsync(
                snapshot, target.Unit, name => name, code => code + request.CodeSuffix, ct);
            return Results.Created(Routes.OrgUnit(result.NewRoot.Id),
                new CopyBranchResultDto(
                    result.NewRoot.Id, result.UnitsCreated,
                    [.. skippedExtras, .. result.SkippedLockedSettings, .. (result.SkippedAllocations ?? []).Select(c => AllocationSkipped + c)]));
        }
        catch (BranchTooLargeException)
        {
            return ApiProblems.BadRequest(ProblemCodes.branch_too_large);
        }
        catch (BranchCodeConflictException conflict)
        {
            return ApiProblems.Conflict(ProblemCodes.code_taken, extra: conflict.Codes);
        }
        catch (BranchCourseCodeConflictException conflict)
        {
            return ApiProblems.Conflict(ProblemCodes.course_code_taken, extra: conflict.Codes);
        }
        catch (BranchInvalidException)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Another request took a code between the conflict check and the insert (unique index): same answer, no 500.
            return ApiProblems.Conflict(ProblemCodes.code_taken);
        }
    }

    private static IResult? Validate(CopyBranchRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(request.CodeSuffix) || request.CodeSuffix.Length > MaxCodeLength
            || !CodePattern().IsMatch(request.CodeSuffix))
        {
            errors[nameof(request.CodeSuffix)] = ["code_suffix_invalid"];
        }

        if ((request.NewRootNameAr is not null && request.NewRootNameAr.Trim().Length > 200)
            || (request.NewRootNameEn is not null && request.NewRootNameEn.Trim().Length > 200))
        {
            errors[nameof(request.NewRootNameEn)] = ["name_too_long"];
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
    }
}
