using Lms.Api.Infrastructure;
using Lms.Domain.Files;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Moderation;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Moderation;

/// <summary>
/// Reporting and moderating messages (M6 Task 15, FR-MSG-04, D66, D69, D70, L6, L16). Any participant who is not the sender reports (<c>Messaging.Send</c>); only the Org Admin
/// (<c>Messaging.Moderate</c>) sees the queue, and only the reports at or above whose units they are assigned (a Manager, a Teacher or a Student is 403, another unit or client 404 or zeros).
/// A moderator reads the reported message with its frozen context and nothing else; every detail and attachment read is audited. No moderation route reads, creates or removes a block.
/// </summary>
internal static class ModerationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.MessageReportPattern, ReportAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Send);
        app.MapGet(Routes.ModerationReports, ListAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapGet(Routes.ModerationReportPattern, DetailAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapPost(Routes.ModerationReportResolvePattern, ResolveAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapPost(Routes.ModerationReportsBulkDismiss, BulkDismissAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapGet(Routes.ModerationReportAttachmentPattern, AttachmentAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapGet(Routes.ModerationStats, StatsAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapGet(Routes.ModerationRestrictions, RestrictionsAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
        app.MapDelete(Routes.ModerationRestrictionPattern, LiftAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Moderate);
    }

    private static async Task<IResult> ReportAsync(Guid id, ReportMessageRequest? body, ReportService reports, HttpContext http, CancellationToken ct)
    {
        var result = await reports.CreateAsync(id, body?.Reason, body?.Note, ct);
        if (result.Outcome == ReportOutcome.RateLimited)
        {
            http.Response.Headers.RetryAfter = (result.RetryAfterSeconds ?? 60).ToString(System.Globalization.CultureInfo.InvariantCulture); // L18
        }

        return result.Outcome switch
        {
            ReportOutcome.Created => Results.NoContent(),
            ReportOutcome.Duplicate => ApiProblems.Conflict(ProblemCodes.report_duplicate),
            ReportOutcome.RateLimited => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests", extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.rate_limited }),
            ReportOutcome.OwnMessage => ApiProblems.BadRequest(ProblemCodes.validation_failed, "message: own"),
            ReportOutcome.Invalid => ApiProblems.BadRequest(ProblemCodes.validation_failed, "reason or note: invalid"),
            _ => ApiProblems.NotFound(),
        };
    }

    private static async Task<IResult> ListAsync([AsParameters] MessageReportQuery query, ModerationQueue queue, CancellationToken ct)
    {
        if (query.Status is not null && !ReportStatuses.All.Contains(query.Status) || query.Reason is not null && !ReportReasons.All.Contains(query.Reason))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "status or reason: invalid_value");
        }

        var (items, total) = await queue.ListAsync(query, ct);
        return Results.Ok(new PagedResult<MessageReportListItemDto>(items, total, Math.Max(query.Page, 1), Math.Clamp(query.PageSize, 1, Paging.MaxPageSize)));
    }

    private static async Task<IResult> DetailAsync(Guid id, ModerationQueue queue, CancellationToken ct) =>
        await queue.OpenAsync(id, ct) is { } report ? Results.Ok(report) : ApiProblems.NotFound();

    private static async Task<IResult> ResolveAsync(Guid id, ResolveReportRequest? body, ModerationActionsService actions, ModerationQueue queue, CancellationToken ct)
    {
        if (body is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var outcome = await actions.ResolveAsync(id, body.Action, body.DurationDays, ct);
        if (outcome != ResolveOutcome.Done)
        {
            return outcome switch
            {
                ResolveOutcome.NotFound => ApiProblems.NotFound(),
                ResolveOutcome.Invalid => ApiProblems.BadRequest(ProblemCodes.validation_failed, "action or durationDays: invalid"),
                _ => ApiProblems.Conflict(ProblemCodes.concurrency_conflict),
            };
        }

        var report = await queue.Visible().FirstAsync(r => r.Id == id, ct);
        return Results.Ok(await queue.ToDtoAsync(report, ct));
    }

    private static async Task<IResult> BulkDismissAsync(BulkDismissRequest? body, ModerationActionsService actions, CancellationToken ct)
    {
        if (body?.ReportIds is null || body.ReportIds.Count == 0 || body.ReportIds.Count > ModerationLimits.BulkDismissMax)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "reportIds: 1 to 50");
        }

        await actions.BulkDismissAsync(body.ReportIds, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AttachmentAsync(Guid id, Guid fileId, LmsDbContext db, ModerationQueue queue, IFileStorage storage, HttpContext http, CancellationToken ct)
    {
        var report = await queue.Visible().FirstOrDefaultAsync(r => r.Id == id, ct);
        // Only an attachment of the reported message: any other file of the conversation is 404.
        var attachment = report is null ? null : await db.Set<MessageAttachment>().FirstOrDefaultAsync(a => a.MessageId == report.MessageId && a.FileId == fileId, ct);
        if (report is null || attachment is null)
        {
            return ApiProblems.NotFound();
        }

        Uri url;
        try
        {
            url = await storage.GetDownloadUrlAsync(fileId, TimeSpan.FromMinutes(ExcuseLimits.AttachmentUrlMinutes), ct);
        }
        catch (StoredFileNotFoundException)
        {
            return ApiProblems.NotFound();
        }

        queue.WriteRead(report, "Attachment", fileId);
        await db.SaveChangesAsync(ct);
        http.Response.Headers.CacheControl = "private, no-store"; // the signed url is never cached (checklist #762)
        var inline = ExcuseLimits.InlinePreviewContentTypes.Contains(attachment.ContentType);
        return Results.Ok(new ModerationAttachmentUrlDto(url.ToString(), DateTime.UtcNow.AddMinutes(ExcuseLimits.AttachmentUrlMinutes), attachment.FileName, attachment.ContentType, inline));
    }

    private static async Task<IResult> StatsAsync(ModerationQueue queue, CancellationToken ct) => Results.Ok(await queue.StatsAsync(ct));

    private static async Task<IResult> RestrictionsAsync(int? page, int? pageSize, ModerationQueue queue, CancellationToken ct) =>
        Results.Ok(await queue.RestrictionsAsync(page ?? 1, pageSize ?? Paging.DefaultPageSize, ct));

    private static async Task<IResult> LiftAsync(Guid userId, ModerationActionsService actions, CancellationToken ct) =>
        await actions.LiftRestrictionAsync(userId, ct) ? Results.NoContent() : ApiProblems.NotFound();
}
