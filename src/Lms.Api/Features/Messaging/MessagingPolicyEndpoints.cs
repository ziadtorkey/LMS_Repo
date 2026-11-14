using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Messaging.Policy;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Messaging;

/// <summary>
/// The safeguarding rules and their reads (M6 Task 11, FR-MSG-02, D69, L4): the client's narrowed matrix (Org Admin at the client root only), the caller's rules summary and
/// limits, recipient search, and the caller's own blocks. A conversation is the only gate for who is read; nothing here lists anyone's conversations. Blocks belong to the
/// blocker: there is no route that reads or removes another user's block, and the blocked person is never told.
/// </summary>
internal static class MessagingPolicyEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.CommsMessagingRules, GetRulesAsync).RequireAuthorization().RequirePermission(Permissions.Comms.Manage);
        app.MapPut(Routes.CommsMessagingRules, SaveRulesAsync).RequireAuthorization().RequirePermission(Permissions.Comms.Manage);
        app.MapGet(Routes.MessagingRulesSummary, SummaryAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.View);
        app.MapGet(Routes.MessagingLimits, LimitsAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.View);
        app.MapGet(Routes.MessagingRecipients, RecipientsAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.View);
        app.MapGet(Routes.MyBlocks, BlocksAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.View);
        app.MapPut(Routes.MyBlockPattern, BlockAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Send);
        app.MapDelete(Routes.MyBlockPattern, UnblockAsync).RequireAuthorization().RequirePermission(Permissions.Messaging.Send);
    }

    private static async Task<IResult> GetRulesAsync(HttpContext http, ICurrentContext current, MessagingRulesService rules, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.Comms.Manage, ct) is { } refused)
        {
            return refused;
        }

        return Results.Ok(await rules.GetAsync(current.OrganizationId!.Value, ct));
    }

    private static async Task<IResult> SaveRulesAsync(SaveMessagingRulesRequest? body, HttpContext http, ICurrentContext current, MessagingRulesService rules, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.Comms.Manage, ct) is { } refused)
        {
            return refused;
        }

        if (body?.Cells is null || body.Cells.Count == 0)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "cells: required");
        }

        var organizationId = current.OrganizationId!.Value;
        var result = await rules.SaveAsync(organizationId, body.Cells, ct);
        return result.Ok
            ? Results.Ok(await rules.GetAsync(organizationId, ct))
            : ApiProblems.BadRequest(ProblemCodes.rules_looser_than_default, string.Join(", ", result.Loosened.Select(c => $"{c.FromRole}>{c.ToRole}")));
    }

    private static async Task<IResult> SummaryAsync(ICurrentContext current, RulesSummaryBuilder summary, CancellationToken ct) =>
        current.UserId is { } me && current.OrganizationId is { } org ? Results.Ok(await summary.BuildAsync(org, me, ct)) : ApiProblems.NotFound();

    private static async Task<IResult> LimitsAsync(ICurrentContext current, ISettingsResolver settings, CancellationToken ct)
    {
        if (current.ScopePath is not { } path)
        {
            return ApiProblems.NotFound();
        }

        return Results.Ok(new MessagingLimitsDto(
            MessagingLimits.MaxBodyLength, MessagingLimits.MaxAttachmentsPerMessage, MessagingLimits.MaxAttachmentMb,
            (await settings.ResolveAsync(SettingKeys.MessagingMaxGroupSize, path, ct)).Value, MessagingLimits.DeleteWindowMinutes,
            (await settings.ResolveAsync(SettingKeys.MessagingMaxPerMinute, path, ct)).Value, (await settings.ResolveAsync(SettingKeys.MessagingReadReceipts, path, ct)).Value));
    }

    private static async Task<IResult> RecipientsAsync(
        string? search, int? page, int? pageSize, ICurrentContext current, RecipientSearch recipients, CancellationToken ct) =>
        current.UserId is { } me && current.OrganizationId is { } org
            ? Results.Ok(await recipients.SearchAsync(org, me, search, page ?? 1, pageSize ?? MessagingLimits.SearchMaxResults, ct))
            : ApiProblems.NotFound();

    private static async Task<IResult> BlocksAsync(LmsDbContext db, ICurrentContext current, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return ApiProblems.NotFound();
        }

        var blocks = await db.Set<UserBlock>().Where(b => b.BlockerUserId == me).OrderBy(b => b.CreatedAt).ToListAsync(ct);
        var ids = blocks.Select(b => b.BlockedUserId).ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return Results.Ok(blocks.Select(b =>
        {
            users.TryGetValue(b.BlockedUserId, out var user);
            return new BlockDto(b.BlockedUserId, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, b.CreatedAt);
        }).ToList());
    }

    private static async Task<IResult> BlockAsync(Guid userId, LmsDbContext db, ICurrentContext current, IMessagingPolicy policy, CancellationToken ct)
    {
        if (current.UserId is not { } me || current.OrganizationId is not { } org)
        {
            return ApiProblems.NotFound();
        }

        if (userId == me)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "userId: invalid");
        }

        // One neutral answer for an unknown user and for one the caller could not message: nothing here confirms that a user exists.
        if (!(await policy.CanStartAsync(me, userId, ct)).Allowed && !await db.Set<UserBlock>().AnyAsync(b => b.BlockerUserId == me && b.BlockedUserId == userId, ct))
        {
            return ApiProblems.NotFound();
        }

        if (!await db.Set<UserBlock>().AnyAsync(b => b.BlockerUserId == me && b.BlockedUserId == userId, ct))
        {
            db.Add(new UserBlock(org, me, userId));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.ToString().Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
            {
                // the same block was created by a second request at once: it is there, which is what was asked
            }
        }

        return Results.NoContent();
    }

    private static async Task<IResult> UnblockAsync(Guid userId, LmsDbContext db, ICurrentContext current, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return ApiProblems.NotFound();
        }

        var block = await db.Set<UserBlock>().FirstOrDefaultAsync(b => b.BlockerUserId == me && b.BlockedUserId == userId, ct);
        if (block is not null)
        {
            db.Remove(block);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }
}
