using Lms.Domain.Abstractions;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms.Push;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Comms;

/// <summary>
/// The caller's Web Push subscriptions (M6 Task 8). No permission: they only ever touch the caller's own rows of the active client, so authentication is the
/// whole gate. The endpoint and the keys are never returned; a subscription of another user is a 404.
/// </summary>
public static class PushSubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapPushSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MyVapidPublicKey, (IPushProvider provider) =>
            provider.PublicKey is { } key ? Results.Ok(new VapidPublicKeyDto(key)) : Problem(StatusCodes.Status404NotFound, ProblemCodes.not_found)).RequireAuthorization();
        app.MapGet(Routes.MyPushSubscriptions, ListAsync).RequireAuthorization();
        app.MapPost(Routes.MyPushSubscriptions, SaveAsync).RequireAuthorization();
        app.MapDelete(Routes.MyPushSubscriptionPattern, DeleteAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListAsync(LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var rows = await db.Set<PushSubscription>().AsNoTracking()
            .Where(s => s.UserId == context.UserId)
            .OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<IResult> SaveAsync(
        SavePushSubscriptionRequest request, LmsDbContext db, ICurrentContext context, PushKeyProtector protector, IClock clock, IOptions<PushOptions> options, CancellationToken ct)
    {
        if (context.UserId is not { } userId || context.OrganizationId is not { } organizationId)
        {
            return Problem(StatusCodes.Status404NotFound, ProblemCodes.not_found); // no active client: nothing of the caller to save into
        }

        if (!IsValid(request, options.Value))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity, title: "push_subscription_invalid",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed, ["reason"] = "push_subscription_invalid" });
        }

        // One row per browser: saving the same endpoint again (the keys rotated, or another person signed in on the device) replaces the old row.
        var hash = PushSubscription.HashOf(request.Endpoint);
        var existing = await db.Set<PushSubscription>().Where(s => s.OrganizationId == organizationId && s.EndpointHash == hash).ToListAsync(ct);
        db.RemoveRange(existing);
        var label = string.IsNullOrWhiteSpace(request.DeviceLabel) ? "Browser" : request.DeviceLabel.Trim();
        var row = new PushSubscription(
            organizationId, userId, request.Endpoint, protector.Protect(request.P256dh), protector.Protect(request.Auth),
            label.Length > PushSubscription.MaxLabelLength ? label[..PushSubscription.MaxLabelLength] : label, clock.UtcNow);
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.MyPushSubscription(row.Id), ToDto(row));
    }

    private static async Task<IResult> DeleteAsync(Guid id, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        var row = await db.Set<PushSubscription>().SingleOrDefaultAsync(s => s.Id == id && s.UserId == context.UserId, ct);
        if (row is null)
        {
            return Problem(StatusCodes.Status404NotFound, ProblemCodes.not_found);
        }

        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool IsValid(SavePushSubscriptionRequest r, PushOptions options) =>
        !string.IsNullOrWhiteSpace(r.Endpoint) && r.Endpoint.Length <= PushSubscription.MaxEndpointLength
        && PushEndpointPolicy.IsAllowed(r.Endpoint, options.AllowedHostSuffixes)
        && !string.IsNullOrWhiteSpace(r.P256dh) && r.P256dh.Length <= PushSubscription.MaxKeyLength
        && !string.IsNullOrWhiteSpace(r.Auth) && r.Auth.Length <= PushSubscription.MaxKeyLength;

    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static PushSubscriptionDto ToDto(PushSubscription s) => new(s.Id, s.DeviceLabel, s.CreatedAtUtc, s.LastSuccessUtc);
}
