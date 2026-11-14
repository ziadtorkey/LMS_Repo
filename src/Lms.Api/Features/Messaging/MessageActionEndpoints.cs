using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Messaging;

/// <summary>
/// Delete for everyone, mute, leave and attachments (FR-MSG-01, L12, L20). Participant-only (L6) and behind the Messaging flag, like every conversation route. The attachment
/// routes and the unmute route are not in the wave 1 contract yet: their patterns live here until the Lead adds them to <c>Routes.Messaging</c>.
/// </summary>
public static class MessageActionEndpoints
{
    public const string AttachmentsPattern = Routes.ConversationPattern + "/attachments";
    public const string AttachmentUrlPattern = Routes.MessagePattern + "/attachments/{fid:guid}/url";
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapMessageActions(this IEndpointRouteBuilder app)
    {
        app.MapDelete(Routes.MessagePattern, DeleteAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapPut(Routes.ConversationMutePattern, MuteAsync).RequirePermission(Permissions.Messaging.View);
        app.MapDelete(Routes.ConversationMutePattern, UnmuteAsync).RequirePermission(Permissions.Messaging.View);
        app.MapPost(Routes.ConversationLeavePattern, LeaveAsync).RequirePermission(Permissions.Messaging.View);
        app.MapPost(AttachmentsPattern, UploadAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapGet(AttachmentUrlPattern, UrlAsync).RequirePermission(Permissions.Messaging.View);
        return app;
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, Guid mid, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, MessageService messages, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var result = await messages.DeleteAsync(opened, mid, ct);
        return result.Message is null ? ConversationEndpoints.Problem(result) : Results.NoContent();
    }

    private static async Task<IResult> MuteAsync(
        Guid id, MuteConversationRequest? body, HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, IClock clock, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        if (body?.UntilUtc is { } until && until <= clock.UtcNow)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A mute must end in the future.");
        }

        opened.Me.Mute(true, body?.UntilUtc);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnmuteAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        opened.Me.Mute(false, null);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> LeaveAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, IClock clock, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        // Only a group can be left; a direct conversation stays (mute it instead). A member who already left has nothing more to leave.
        if (opened.Conversation.Kind != ConversationKinds.Group || opened.Me.HasLeft)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Only a group you are still in can be left.");
        }

        opened.Me.Leave(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UploadAsync(
        Guid id, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, MessageAttachmentGuard guard, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        // Participant first: a non-participant never makes the server read a body.
        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var (form, problem) = await UploadForm.ReadAsync(http, ct);
        if (form is null)
        {
            return problem!;
        }

        var file = form.Files.FirstOrDefault();
        if (file is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A file is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await guard.UploadAsync(opened, file.FileName, file.Length, stream, ct);
        if (result.Attachment is { } attachment)
        {
            return Results.Created($"{Routes.Conversation(id)}/attachments/{attachment.FileId}", new MessageAttachmentDto(attachment.FileId, attachment.FileName, attachment.ContentType, attachment.SizeBytes));
        }

        if (result.Status == 429)
        {
            http.Response.Headers.RetryAfter = (result.RetryAfterSeconds ?? 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: 429, title: "Too many requests", extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.rate_limited });
        }

        return ConversationEndpoints.Problem(new SendResult(null, result.ProblemCode, result.Status));
    }

    private static async Task<IResult> UrlAsync(
        Guid id, Guid mid, Guid fid, HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, IFileStorage storage, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        // The participant check above is the access check: the file must belong to a live message of this conversation, sent before a leaving member left.
        var attachment = await db.Set<MessageAttachment>().SingleOrDefaultAsync(a => a.FileId == fid && a.MessageId == mid && a.ConversationId == id, ct);
        var message = attachment is null ? null : await db.Set<Message>().SingleOrDefaultAsync(m => m.Id == mid && m.ConversationId == id, ct);
        if (attachment is null || message is null || message.State != Message.NormalState || (opened.Me.LeftAtUtc is { } left && message.SentAtUtc > left))
        {
            return ApiProblems.NotFound();
        }

        var url = await storage.GetDownloadUrlAsync(fid, UrlLifetime, ct); // a forced download, valid 5 minutes
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Ok(new { url = url.ToString(), expiresAtUtc = DateTime.UtcNow.Add(UrlLifetime), fileName = attachment.FileName, contentType = attachment.ContentType });
    }
}
