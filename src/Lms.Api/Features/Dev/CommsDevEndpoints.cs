using Lms.Api.Infrastructure;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Dev;

/// <summary>
/// Development-only trigger of the communication channels (M6 Task 18): see what the fake channels were asked to send, clear them and script their next
/// outcomes, so an end-to-end test (and a developer) can drive retries, bounces and caps without a relay. The routes do not exist outside Development and
/// need a platform admin; the listing shows the channel, the template or event key and a masked address, never a message text.
/// </summary>
public sealed class CommsDevModule : ILmsModule
{
    /// <summary>POST <see cref="ScriptRequest"/>: queues the next outcomes of a fake channel. Not part of Lms.Shared on purpose (Development only).</summary>
    public const string ScriptRoute = "/api/dev/comms/script";

    public sealed record ScriptRequest(string Channel, string Outcome, string? ErrorCode = null, int? RetryAfterSeconds = null, int Count = 1);

    public sealed record SentItem(string Channel, Guid OutboxId, Guid UserId, string? EventKey, string? TemplateKey, string MaskedAddress);

    public sealed record OutboxRow(Guid Id, string Channel, string Status, string? TemplateKey, int Attempts, string? LastErrorCode, DateTime CreatedAtUtc);

    public sealed record OutboxListing(IReadOnlyList<SentItem> Sent, IReadOnlyList<OutboxRow> Rows);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        app.MapGet(Routes.DevCommsOutbox, ListAsync).RequireAuthorization().RequirePermission(Permissions.Clients.Update);
        app.MapDelete(Routes.DevCommsOutbox, Clear).RequireAuthorization().RequirePermission(Permissions.Clients.Update);
        app.MapPost(ScriptRoute, Script).RequireAuthorization().RequirePermission(Permissions.Clients.Update);
    }

    private static async Task<IResult> ListAsync(LmsDbContext db, FakeDeliveryChannels? fakes, CancellationToken ct)
    {
        var rows = await db.Set<DeliveryOutbox>().AsNoTracking().OrderByDescending(r => r.CreatedAtUtc).Take(100)
            .Select(r => new OutboxRow(r.Id, r.Channel, r.Status, r.TemplateKey, r.Attempts, r.LastErrorCode, r.CreatedAtUtc)).ToListAsync(ct);
        var sent = (fakes?.All.OfType<FakeDeliveryChannel>() ?? [])
            .SelectMany(c => c.Sent.Select(s => new SentItem(c.Channel, s.OutboxId, s.UserId, s.EventKey, s.TemplateKey, Mask(s.Address))))
            .ToList();
        return Results.Ok(new OutboxListing(sent, rows));
    }

    private static IResult Clear(FakeDeliveryChannels? fakes)
    {
        if (fakes is null)
        {
            return ApiProblems.NotFound();
        }

        fakes.Reset();
        return Results.NoContent();
    }

    private static IResult Script(ScriptRequest? body, FakeDeliveryChannels? fakes)
    {
        var channel = fakes?.All.OfType<FakeDeliveryChannel>().FirstOrDefault(c => c.Channel == body?.Channel);
        if (body is null || channel is null || body.Count is < 1 or > 100 || ToOutcome(body) is not { } outcome)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Channel must be Email or Push, Outcome Accepted, Transient, Permanent or NotConfigured, Count 1 to 100.");
        }

        for (var i = 0; i < body.Count; i++)
        {
            channel.Enqueue(outcome);
        }

        return Results.NoContent();
    }

    private static DeliveryOutcome? ToOutcome(ScriptRequest r) => r.Outcome switch
    {
        nameof(DeliveryOutcomeKind.Accepted) => DeliveryOutcome.Accepted("scripted"),
        nameof(DeliveryOutcomeKind.Transient) => DeliveryOutcome.Transient(r.ErrorCode ?? "scripted_transient", r.RetryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null),
        nameof(DeliveryOutcomeKind.Permanent) => DeliveryOutcome.Permanent(r.ErrorCode ?? "scripted_permanent"),
        nameof(DeliveryOutcomeKind.NotConfigured) => DeliveryOutcome.NotConfigured(),
        _ => null,
    };

    /// <summary><c>m***@e***.test</c>: enough to tell the addresses apart in a test, never the address.</summary>
    public static string Mask(string address)
    {
        var at = address.IndexOf('@');
        if (at <= 0)
        {
            return address.Length == 0 ? string.Empty : address[0] + "***";
        }

        var domain = address[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return address[0] + "***@" + (domain.Length > 0 ? domain[0] + "***" : string.Empty) + (dot > 0 ? domain[dot..] : string.Empty);
    }
}
