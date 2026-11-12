using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Forums;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>Validation and access helpers shared by the thread, post and announcement endpoints (FR-FOR-01).</summary>
internal static class ForumSupport
{
    public const int MaxTitle = 200;
    public const int MaxAttachments = 5;

    public static IResult Invalid(string field, string reason = "invalid") =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [reason] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static IResult Forbidden(HttpContext http) =>
        AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http);

    /// <summary>The title is user text in the author's language: 1 to 200 characters after trimming.</summary>
    public static IResult? ValidateTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitle ? Invalid("title", "length") : null;

    /// <summary>Sanitises a body on write (D48). A body that is empty after sanitising, or over 200 000 characters, is a 400.</summary>
    public static IResult? SanitizeBody(IRichTextSanitizer sanitizer, string? html, out string clean)
    {
        clean = string.Empty;
        try
        {
            clean = sanitizer.Sanitize(html);
        }
        catch (RichTextTooLongException)
        {
            return Invalid("bodyHtml", "too_long");
        }

        return string.IsNullOrWhiteSpace(StripTags(clean)) ? Invalid("bodyHtml", "required") : null;
    }

    /// <summary>
    /// At most five distinct attachments, each an asset of this ClassCourse (the caller can only attach what belongs to the
    /// class course whose forum they are writing in).
    /// </summary>
    public static async Task<IResult?> ValidateAttachmentsAsync(
        IReadOnlyList<Guid>? assetIds, Guid classCourseId, LmsDbContext db, CancellationToken ct)
    {
        var ids = (assetIds ?? []).Distinct().ToList();
        if (ids.Count > MaxAttachments)
        {
            return Invalid("attachmentAssetIds", "too_many");
        }

        if (ids.Count == 0)
        {
            return null;
        }

        var found = await db.Set<Asset>().CountAsync(a => ids.Contains(a.Id) && a.ClassCourseId == classCourseId, ct);
        return found == ids.Count ? null : Invalid("attachmentAssetIds", "not_found");
    }

    /// <summary>Does the caller hold the permission on this ClassCourse (unit scope and relationship rule)?</summary>
    public static async Task<bool> HoldsAsync(HttpContext http, string permission, Guid classCourseId, CancellationToken ct) =>
        (await http.CheckClassCourseAsync(permission, classCourseId, ct)).Problem is null;

    /// <summary>A write needs an Active class in a year that is not Closed (409 class_closed / year_closed).</summary>
    public static Task<IResult?> WritableAsync(ClassCourse classCourse, LmsDbContext db, PlacementGuard guard, CancellationToken ct) =>
        GroupEndpoints.WritableAsync(classCourse, db, guard, ct);

    public static (int Page, int Size, IResult? Problem) Paging(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? Lms.Shared.Paging.DefaultPageSize;
        return p < 1 || size < 1 || size > Lms.Shared.Paging.MaxPageSize
            ? (p, size, Results.ValidationProblem(
                new Dictionary<string, string[]> { [p < 1 ? "page" : "pageSize"] = ["paging_invalid"] },
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed }))
            : (p, size, null);
    }

    private static string StripTags(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
}
