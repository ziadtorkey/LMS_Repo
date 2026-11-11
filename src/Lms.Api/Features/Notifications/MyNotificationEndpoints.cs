using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// The notification center of the caller. These endpoints carry no permission: they only ever touch the caller's own
/// rows (UserId == the token's user) of the active client, so authentication is the whole gate.
/// </summary>
public static class MyNotificationEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapMyNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MyNotifications, ListAsync).RequireAuthorization();
        app.MapPost(Routes.MyNotificationReadPattern, MarkReadAsync).RequireAuthorization();
        app.MapPost(Routes.MyNotificationsReadAll, ReadAllAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListAsync(
        LmsDbContext db, ICurrentContext context, bool? unreadOnly, int? page, int? pageSize, CancellationToken ct)
    {
        var pageNumber = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (pageNumber < 1 || size < 1 || size > MaxPageSize)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [pageNumber < 1 ? "page" : "pageSize"] = [pageNumber < 1 ? "page_must_be_at_least_1" : "page_size_must_be_1_to_200"],
                },
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
        }

        // The tenant filter of the table limits this to the active client; the user filter limits it to the caller.
        var own = db.Set<Notification>().Where(x => x.UserId == context.UserId);
        var unreadCount = await own.CountAsync(x => x.ReadAt == null, ct);
        var matching = unreadOnly == true ? own.Where(x => x.ReadAt == null) : own;
        var total = await matching.CountAsync(ct);
        var rows = await matching
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((pageNumber - 1) * size).Take(size)
            .ToListAsync(ct);

        return Results.Ok(new NotificationPageDto(rows.Select(ToDto).ToList(), total, unreadCount));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id, LmsDbContext db, ICurrentContext context, IClock clock, CancellationToken ct)
    {
        var row = await db.Set<Notification>().SingleOrDefaultAsync(x => x.Id == id && x.UserId == context.UserId, ct);
        if (row is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Not found",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.not_found });
        }

        row.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReadAllAsync(LmsDbContext db, ICurrentContext context, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.Set<Notification>()
            .Where(x => x.UserId == context.UserId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now), ct);
        return Results.NoContent();
    }

    private static NotificationDto ToDto(Notification x) => new(
        x.Id,
        x.EventKey,
        JsonSerializer.Deserialize<Dictionary<string, string>>(x.ParametersJson) ?? [],
        x.DeepLink,
        x.CreatedAt,
        x.ReadAt is not null);
}
