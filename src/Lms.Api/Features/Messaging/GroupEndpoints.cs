using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Messaging;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;

namespace Lms.Api.Features.Messaging;

/// <summary>Group members and the read-only switch (FR-MSG-01, D74). Owner only; any other participant, and everyone else, gets 404 (L6).</summary>
public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ConversationParticipantsPattern, AddAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapDelete(Routes.ConversationParticipantPattern, RemoveAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapPut(Routes.ConversationReadOnlyPattern, SetReadOnlyAsync).RequirePermission(Permissions.Messaging.Send);
        app.MapDelete(Routes.ConversationReadOnlyPattern, ClearReadOnlyAsync).RequirePermission(Permissions.Messaging.Send);
        return app;
    }

    /// <summary>The group branch of <c>POST /api/conversations</c>.</summary>
    internal static async Task<IResult> CreateGroupAsync(
        CreateConversationRequest body, GroupService groups, MessageService messages, ConversationAccess access, ConversationMapper mapper, CancellationToken ct)
    {
        if (body.ClassCourseId is not null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Class and course groups are not available yet."); // the teacher-of-the-class check belongs to the D69 rules (Task 11)
        }

        var created = await groups.CreateAsync(body.Title, body.ParticipantUserIds, ct);
        if (created.Conversation is not { } group)
        {
            return Problem(created);
        }

        var opened = await access.OpenAsync(group.Id, ct);
        if (opened is null)
        {
            return ApiProblems.NotFound();
        }

        if (!string.IsNullOrWhiteSpace(body.FirstMessage) && await messages.SendAsync(opened, body.FirstMessage, ct) is { Message: null } refused)
        {
            return ConversationEndpoints.Problem(refused);
        }

        return Results.Created(Routes.Conversation(group.Id), await mapper.ToDtoAsync(opened, ct));
    }

    private static async Task<IResult> AddAsync(
        Guid id, AddParticipantsRequest? body, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, GroupService groups, ConversationMapper mapper, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var result = await groups.AddAsync(opened, body?.UserIds, ct);
        return result.Conversation is null ? Problem(result) : Results.Ok(await mapper.ToDtoAsync(opened, ct));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, Guid userId, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, GroupService groups, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var result = await groups.RemoveAsync(opened, userId, ct);
        return result.Conversation is null ? Problem(result) : Results.NoContent();
    }

    private static Task<IResult> SetReadOnlyAsync(
        Guid id, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, GroupService groups, ConversationMapper mapper, CancellationToken ct) =>
        ReadOnlyAsync(id, true, http, context, settings, access, groups, mapper, ct);

    private static Task<IResult> ClearReadOnlyAsync(
        Guid id, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, GroupService groups, ConversationMapper mapper, CancellationToken ct) =>
        ReadOnlyAsync(id, false, http, context, settings, access, groups, mapper, ct);

    private static async Task<IResult> ReadOnlyAsync(
        Guid id, bool readOnly, HttpContext http, ICurrentContext context, ISettingsResolver settings, ConversationAccess access, GroupService groups, ConversationMapper mapper, CancellationToken ct)
    {
        if (await ConversationEndpoints.GateAsync(http, context, settings, ct) is { } gated)
        {
            return gated;
        }

        if (await access.OpenAsync(id, ct) is not { } opened)
        {
            return ApiProblems.NotFound();
        }

        var result = await groups.SetReadOnlyAsync(opened, readOnly, ct);
        return result.Conversation is null ? Problem(result) : Results.Ok(await mapper.ToDtoAsync(opened, ct));
    }

    private static IResult Problem(GroupResult result) => result.Status switch
    {
        400 => ApiProblems.BadRequest(result.ProblemCode!),
        404 => ApiProblems.NotFound(),
        _ => Results.Problem(statusCode: result.Status, title: "Not allowed", extensions: new Dictionary<string, object?> { ["code"] = result.ProblemCode }),
    };
}
