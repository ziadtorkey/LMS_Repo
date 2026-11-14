using Lms.Api.Features.Forums;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Announcements;
using Lms.Domain.Content;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Announcements;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Announcements;

/// <summary>
/// The announcement store under <c>/api/comms/announcements</c> (M6 Task 13). Capabilities, list, read, create and edit, delete a draft, audience preview, publish (now or scheduled) and withdraw.
/// An unrestricted grant (a unit admin) sees every announcement in scope; a restricted one (a Teacher) sees only their own.
/// </summary>
internal static class AnnouncementEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgAnnouncementCapabilities, CapabilitiesAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.View);
        app.MapGet(Routes.OrgAnnouncements, ListAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapPost(Routes.OrgAnnouncements, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapGet(Routes.OrgAnnouncementPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapPut(Routes.OrgAnnouncementPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapDelete(Routes.OrgAnnouncementPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapPost(Routes.OrgAnnouncementPublishPattern, PublishAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapPost(Routes.OrgAnnouncementWithdrawPattern, WithdrawAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
        app.MapPost(Routes.OrgAnnouncementAudiencePreview, PreviewAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.Manage);
    }

    private static async Task<IResult> CapabilitiesAsync(HttpContext http, LmsDbContext db, ICurrentContext context, TargetScopeGuard guard, CancellationToken ct)
    {
        var scopePath = context.ScopePath;
        var unit = scopePath is null ? null : await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Path == scopePath, ct);
        var mayManage = unit is not null && (await http.CheckUnitAsync(TargetScopeGuard.Permission, unit.Id, ct)).Problem is null;
        var unrestricted = mayManage && await guard.IsUnrestrictedAsync(ct);
        return Results.Ok(new AnnouncementCapabilitiesDto(unrestricted, mayManage, unrestricted));
    }

    private static async Task<IResult> ListAsync(
        string? status, int? page, int? pageSize, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, AnnouncementMapper mapper, CancellationToken ct)
    {
        var (p, size, invalid) = ForumSupport.Paging(page, pageSize);
        if (invalid is not null || (status is not null && !AnnouncementStatuses.All.Contains(status)))
        {
            return invalid ?? ForumSupport.Invalid("status");
        }

        var query = await VisibleAsync(db, context, guard, ct);
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<AnnouncementListItemDto>(await mapper.ToListItemsAsync(rows, ct), await query.CountAsync(ct), p, size));
    }

    private static async Task<IResult> GetAsync(
        Guid id, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, AnnouncementMapper mapper, CancellationToken ct)
    {
        var row = await (await VisibleAsync(db, context, guard, ct)).FirstOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? ApiProblems.NotFound() : Results.Ok((await mapper.ToDtosAsync([row], ct))[0]);
    }

    private static async Task<IResult> CreateAsync(
        CreateAnnouncementRequest? request, HttpContext http, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, IRichTextSanitizer sanitizer,
        AnnouncementMapper mapper, AnnouncementService service, CancellationToken ct)
    {
        if (request is null || context.UserId is not { } userId)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var (unit, unitProblem) = await guard.AuthoringUnitAsync(http, request.AuthoringUnitId, ct);
        if (unitProblem is not null)
        {
            return unitProblem;
        }

        var (clean, problem) = await ValidateAsync(
            request.TitleAr, request.TitleEn, request.BodyAr, request.BodyEn, request.Targets, request.RoleFilters, request.PublishAtUtc, request.ExpiresAtUtc,
            request.IsUrgent, http, guard, sanitizer, ct);
        if (problem is not null)
        {
            return problem;
        }

        var row = new Announcement(unit!, userId);
        row.SetContent(
            request.TitleAr, request.TitleEn, clean!.BodyAr, clean.BodyEn, await ChipsAsync(request.RoleFilters, guard, ct), request.PublishAtUtc,
            request.ExpiresAtUtc, request.IsPinned, request.Notify, request.IsUrgent);
        if (request.Publish && !await service.UnderAuthorCapAsync(row, ct))
        {
            return ApiProblems.TooManyRequests(ProblemCodes.rate_limited);
        }

        db.Set<Announcement>().Add(row);
        db.Set<AnnouncementTarget>().AddRange(request.Targets.Select(t => new AnnouncementTarget(row, t.Kind, t.TargetId, t.IncludeSubtree)));
        await db.SaveChangesAsync(ct);
        if (request.Publish)
        {
            await service.PublishAsync(row, ct);
        }

        return Results.Created(Routes.OrgAnnouncement(row.Id), (await mapper.ToDtosAsync([row], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateAnnouncementRequest? request, HttpContext http, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard,
        IRichTextSanitizer sanitizer, IClock clock, AnnouncementMapper mapper, AnnouncementService service, CancellationToken ct)
    {
        var row = await (await VisibleAsync(db, context, guard, ct)).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        if (request is null)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var (clean, problem) = await ValidateAsync(
            request.TitleAr, request.TitleEn, request.BodyAr, request.BodyEn, request.Targets, request.RoleFilters, request.PublishAtUtc, request.ExpiresAtUtc,
            request.IsUrgent, http, guard, sanitizer, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (row.IsEditable)
        {
            if (row.Status == AnnouncementStates.Scheduled && (request.PublishAtUtc is not { } at || at <= clock.UtcNow))
            {
                return ForumSupport.Invalid("publishAtUtc", "must_be_later"); // withdraw cancels a schedule; an edit keeps it ahead
            }

            row.SetContent(
                request.TitleAr, request.TitleEn, clean!.BodyAr, clean.BodyEn, await ChipsAsync(request.RoleFilters, guard, ct), request.PublishAtUtc, request.ExpiresAtUtc,
                request.IsPinned, request.Notify, request.IsUrgent);
            db.Set<AnnouncementTarget>().RemoveRange(await db.Set<AnnouncementTarget>().Where(t => t.AnnouncementId == row.Id).ToListAsync(ct));
            db.Set<AnnouncementTarget>().AddRange(request.Targets.Select(t => new AnnouncementTarget(row, t.Kind, t.TargetId, t.IncludeSubtree)));
        }
        else if (row.Status == AnnouncementStates.Published)
        {
            row.EditPublished(clean!.BodyAr, clean.BodyEn, request.ExpiresAtUtc, request.IsPinned, clock.UtcNow);
        }
        else
        {
            return ApiProblems.Conflict(ProblemCodes.announcement_not_editable);
        }

        await db.SaveChangesAsync(ct);
        if (row.Status == AnnouncementStates.Scheduled)
        {
            service.Reschedule(row); // the keyed job moves, it is never duplicated
        }

        return Results.Ok((await mapper.ToDtosAsync([row], ct))[0]);
    }

    private static async Task<IResult> PublishAsync(
        Guid id, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, AnnouncementService service, AnnouncementMapper mapper, CancellationToken ct)
    {
        var row = await (await VisibleAsync(db, context, guard, ct)).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        if (row.Status != AnnouncementStates.Draft)
        {
            return ApiProblems.Conflict(ProblemCodes.announcement_not_editable);
        }

        if (!await service.UnderAuthorCapAsync(row, ct))
        {
            return ApiProblems.TooManyRequests(ProblemCodes.rate_limited);
        }

        await service.PublishAsync(row, ct);
        return Results.Ok((await mapper.ToDtosAsync([row], ct))[0]);
    }

    private static async Task<IResult> WithdrawAsync(
        Guid id, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, AnnouncementService service, AnnouncementMapper mapper, CancellationToken ct)
    {
        var row = await (await VisibleAsync(db, context, guard, ct)).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        if (row.Status is not (AnnouncementStates.Scheduled or AnnouncementStates.Published))
        {
            return ApiProblems.Conflict(ProblemCodes.announcement_not_editable);
        }

        await service.WithdrawAsync(row, ct);
        return Results.Ok((await mapper.ToDtosAsync([row], ct))[0]);
    }

    private static async Task<IResult> PreviewAsync(
        AudiencePreviewRequest? request, HttpContext http, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, AudienceResolver resolver,
        IClock clock, CancellationToken ct)
    {
        if (request is null || !TargetScopeGuard.IsWellFormed(request.Targets))
        {
            return ApiProblems.BadRequest(ProblemCodes.announcement_target_invalid);
        }

        var (previewUnit, unitProblem) = await guard.AuthoringUnitAsync(http, request.AuthoringUnitId, ct);
        if (unitProblem is not null)
        {
            return unitProblem;
        }

        if (await guard.CheckAsync(http, request.Targets, ct) is { } unreachable)
        {
            return unreachable;
        }

        var chips = await ChipsAsync(request.RoleFilters, guard, ct);
        var audience = await resolver.ResolveAsync(
            previewUnit!.OrganizationId, [.. request.Targets.Select(t => new AudienceTarget(t.Kind, t.TargetId, t.IncludeSubtree))], chips, context.UserId, DateOnly.FromDateTime(clock.UtcNow), ct);
        var sampleIds = audience.Users.Keys.Take(10).ToList();
        var names = await db.Set<Lms.Domain.Identity.User>().AsNoTracking().Where(u => sampleIds.Contains(u.Id))
            .Select(u => string.IsNullOrEmpty(u.DisplayNameEn) ? u.DisplayNameAr : u.DisplayNameEn).ToListAsync(ct);
        var byChip = AnnouncementRoleFilters.All.Where(chips.Contains).ToDictionary(c => c, c => audience.Users.Count(u => u.Value == c));
        return Results.Ok(new AudiencePreviewDto(audience.Count, byChip, names));
    }

    private static async Task<IReadOnlyList<string>> ChipsAsync(IReadOnlyList<string>? chosen, TargetScopeGuard guard, CancellationToken ct) =>
        AnnouncementService.EffectiveChips(chosen, await guard.IsUnrestrictedAsync(ct));

    private static async Task<IResult> DeleteAsync(Guid id, ICurrentContext context, LmsDbContext db, TargetScopeGuard guard, CancellationToken ct)
    {
        var row = await (await VisibleAsync(db, context, guard, ct)).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        if (row.Status != AnnouncementStates.Draft)
        {
            return ApiProblems.Conflict(ProblemCodes.announcement_not_editable);
        }

        db.Set<AnnouncementTarget>().RemoveRange(await db.Set<AnnouncementTarget>().Where(t => t.AnnouncementId == row.Id).ToListAsync(ct));
        db.Set<Announcement>().Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>The announcements the caller may see here: all in scope for an unrestricted grant, otherwise only their own.</summary>
    private static async Task<IQueryable<Announcement>> VisibleAsync(LmsDbContext db, ICurrentContext context, TargetScopeGuard guard, CancellationToken ct)
    {
        var all = await guard.IsUnrestrictedAsync(ct);
        var me = context.UserId;
        return db.Set<Announcement>().Where(a => all || a.AuthorUserId == me);
    }

    private sealed record Clean(string? BodyAr, string? BodyEn);

    /// <summary>Titles, bodies, targets, filters, dates and the urgent flag. Returns the sanitised bodies, or the response to send.</summary>
    private static async Task<(Clean? Clean, IResult? Problem)> ValidateAsync(
        string? titleAr, string? titleEn, string? bodyAr, string? bodyEn, IReadOnlyList<AnnouncementTargetDto>? targets, IReadOnlyList<string>? roleFilters,
        DateTime? publishAt, DateTime? expiresAt, bool urgent, HttpContext http, TargetScopeGuard guard, IRichTextSanitizer sanitizer, CancellationToken ct)
    {
        if (ForumSupport.ValidateTitle(titleAr) is not null)
        {
            return (null, ForumSupport.Invalid("titleAr", "length"));
        }

        if (ForumSupport.ValidateTitle(titleEn) is not null)
        {
            return (null, ForumSupport.Invalid("titleEn", "length"));
        }

        string? cleanAr = null;
        string? cleanEn = null;
        if (!string.IsNullOrWhiteSpace(bodyAr) && ForumSupport.SanitizeBody(sanitizer, bodyAr, out cleanAr) is { } badBodyAr)
        {
            return (null, badBodyAr);
        }

        if (!string.IsNullOrWhiteSpace(bodyEn) && ForumSupport.SanitizeBody(sanitizer, bodyEn, out cleanEn) is { } badBodyEn)
        {
            return (null, badBodyEn);
        }

        if (string.IsNullOrWhiteSpace(cleanAr) && string.IsNullOrWhiteSpace(cleanEn))
        {
            return (null, ForumSupport.Invalid("body", "required"));
        }

        if (roleFilters is not null && roleFilters.Any(f => !AnnouncementRoleFilters.All.Contains(f)))
        {
            return (null, ForumSupport.Invalid("roleFilters"));
        }

        if (expiresAt is { } e && (e <= DateTime.UtcNow || (publishAt is { } p && e <= p)))
        {
            return (null, ForumSupport.Invalid("expiresAtUtc", "must_be_later"));
        }

        if (!TargetScopeGuard.IsWellFormed(targets))
        {
            return (null, ApiProblems.BadRequest(ProblemCodes.announcement_target_invalid));
        }

        if (await guard.CheckAsync(http, targets!, ct) is { } unreachable)
        {
            return (null, unreachable);
        }

        if (urgent && !await guard.IsUnrestrictedAsync(ct))
        {
            return (null, ForumSupport.Forbidden(http));
        }

        return (new Clean(string.IsNullOrWhiteSpace(cleanAr) ? null : cleanAr, string.IsNullOrWhiteSpace(cleanEn) ? null : cleanEn), null);
    }
}
