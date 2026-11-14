using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Messaging;

/// <summary>
/// Conversations and messages of the caller (FR-MSG-01, D66, L6). Participant-only: a miss is 404 for everyone else, an Org Admin included. Every route needs the
/// <c>Messaging</c> flag (403 <c>feature_disabled</c>) and answers <c>Cache-Control: private, no-store</c>.
/// </summary>
public static class ConversationEndpoints
{
    private const int ListPageSize = 50;
    private const int ListPageSizeMax = 100;

    public static IEndpointRouteBuilder MapConversations(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.Conversations, CreateAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapGet(Routes.Conversations, ListAsync).RequirePermission(Permissions.Messaging.View);
        app.MapGet(Routes.ConversationPattern, GetAsync).RequirePermission(Permissions.Messaging.View);
        app.MapPost(Routes.ConversationMessagesPattern, SendAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapGet(Routes.ConversationMessagesPattern, MessagesAsync).RequirePermission(Permissions.Messaging.View);
        app.MapPost(Routes.ConversationReadPattern, ReadAsync).RequirePermission(Permissions.Messaging.Send); // the contract: POST, Messaging.Send (it writes)
        app.MapGet(Routes.MyUnreadTotal, UnreadTotalAsync).RequirePermission(Permissions.Messaging.View);
        app.MapMessageActions();
        app.MapGroupEndpoints();
        return app;
    }

    /// <summary>The flag at the caller scope; the answer is private and never cached.</summary>
    internal static async Task<IResult?> GateAsync(HttpContext http, ICurrentContext context, ISettingsResolver settings, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        if (context.UserId is null || context.OrganizationId is null || context.ScopePath is not { } path)
        {
            return ApiProblems.NotFound();
        }

        return (await settings.ResolveAsync(FeatureFlags.Messaging, path, ct)).Value
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Feature disabled", extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.feature_disabled });
    }

    private static IResult NotAllowed() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed", extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.messaging_not_allowed });

    private static async Task<IResult> CreateAsync(
        CreateConversationRequest? body, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationService conversations, IMessagingPolicy policy,
        MessageService messages, ConversationAccess access, ConversationMapper mapper, GroupService groups, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (body is { Kind: ConversationKinds.Group })
        {
            return await GroupEndpoints.CreateGroupAsync(body, groups, messages, access, mapper, ct);
        }

        if (body is null || body.Kind != ConversationKinds.Direct || body.ParticipantUserIds is not { Count: 1 } others || others[0] == context.UserId)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A direct conversation needs exactly one other user.");
        }

        var other = others[0];
        // A person outside this client, or one the rules do not let the caller write to, gets the same answer: no name, no reason.
        if (!await conversations.ExistsInClientAsync(other, ct) || !(await policy.CanStartAsync(context.UserId!.Value, other, ct)).Allowed)
        {
            return NotAllowed();
        }

        var (conversation, created) = await conversations.GetOrCreateDirectAsync(other, ct);
        var opened = await access.OpenAsync(conversation.Id, ct);
        if (opened is null)
        {
            return ApiProblems.NotFound();
        }

        if (!string.IsNullOrWhiteSpace(body.FirstMessage))
        {
            var sent = await messages.SendAsync(opened, body.FirstMessage, ct);
            if (sent.Message is null)
            {
                return Problem(sent);
            }
        }

        var dto = await mapper.ToDtoAsync(opened, ct);
        return created ? Results.Created(Routes.Conversation(conversation.Id), dto) : Results.Ok(dto); // an existing pair answers 200 with it
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ConversationMapper mapper, CancellationToken ct, int page = 1, int pageSize = ListPageSize)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        var me = context.UserId!.Value;
        var size = Math.Clamp(pageSize, 1, ListPageSizeMax);
        var skip = (Math.Max(page, 1) - 1) * size;
        // Newest activity first (a conversation with no message sorts by its creation), the id breaks ties so pages never overlap.
        var mine = await (from p in db.Set<ConversationParticipant>()
                          where p.UserId == me
                          join c in db.Set<Conversation>() on p.ConversationId equals c.Id
                          orderby (c.LastMessageAtUtc ?? c.CreatedAtUtc) descending, c.Id descending
                          select p).Skip(skip).Take(size).ToListAsync(ct);
        return Results.Ok(await mapper.ToListAsync(mine, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, ConversationMapper mapper, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        return await access.OpenAsync(id, ct) is { } opened ? Results.Ok(await mapper.ToDtoAsync(opened, ct)) : ApiProblems.NotFound();
    }

    private static async Task<IResult> SendAsync(
        Guid id, SendMessageRequest? body, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, MessageService messages, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var sent = await messages.SendAsync(opened, body?.Body, body?.AttachmentFileIds, ct);
        return sent.Message is null ? Problem(sent) : Results.Created(Routes.Message(id, sent.Message.Id), (await messages.ToDtosAsync([sent.Message], ct))[0]);
    }

    private static async Task<IResult> MessagesAsync(
        Guid id, [AsParameters] MessageQuery query, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, MessageService messages, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        return await access.OpenAsync(id, ct) is { } opened ? Results.Ok(await messages.PageAsync(opened, query.BeforeId, query.PageSize, ct)) : ApiProblems.NotFound();
    }

    private static async Task<IResult> ReadAsync(Guid id, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, ReadStateService readState, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        await readState.MarkReadAsync(opened, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnreadTotalAsync(
        HttpContext http, LmsDbContext db, ICurrentContext context, ISettingsResolver settings, ReadStateService readState, CancellationToken ct)
    {
        if (await GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        var me = context.UserId!.Value;
        var mine = await db.Set<ConversationParticipant>().Where(p => p.UserId == me).Take(200).ToListAsync(ct);
        var unread = await readState.UnreadByConversationAsync(mine, ct);
        return Results.Ok(new UnreadTotalDto(unread.Count(kv => kv.Value > 0)));
    }

    internal static IResult Problem(SendResult sent) => sent.Status switch
    {
        400 => ApiProblems.BadRequest(sent.ProblemCode!),
        404 => ApiProblems.NotFound(),
        409 => ApiProblems.Conflict(sent.ProblemCode!),
        _ => Results.Problem(statusCode: sent.Status, title: "Not allowed", extensions: new Dictionary<string, object?> { ["code"] = sent.ProblemCode }),
    };
}
