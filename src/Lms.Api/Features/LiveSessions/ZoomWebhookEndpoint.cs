using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Zoom;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LiveSessions;

/// <summary>
/// <c>POST /api/integrations/zoom/webhook</c>: the one anonymous M4b route (D54), called by Zoom. Order of checks: the body is at most
/// 256 KB (413), the signature over the raw bytes and then the five-minute window (a failure is 401 with no hint, or 429 without any
/// database access once the address is over its failed-verification limit; a valid signature is never refused for the address), the
/// global ceiling for verified traffic (429), the
/// URL-validation challenge, then the event: its receipt and its effect are written in <b>one transaction</b>, a re-delivery is a 200
/// no-op, and a failure rolls both back and answers 5xx so Zoom retries.
/// </summary>
public static class ZoomWebhookEndpoint
{
    public const int MaxBodyBytes = 256 * 1024;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(Routes.ZoomWebhook, HandleAsync).AllowAnonymous();

    private static async Task<IResult> HandleAsync(
        HttpContext http, ZoomWebhookVerifier verifier, ZoomWebhookRateLimiter limiter, ZoomEventProcessor processor, LmsDbContext db,
        ILogger<ZoomWebhookLog> logger, CancellationToken ct)
    {
        var address = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var body = await ReadBodyAsync(http, ct);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var verification = verifier.Verify(
            body, http.Request.Headers["x-zm-request-timestamp"].FirstOrDefault(), http.Request.Headers["x-zm-signature"].FirstOrDefault());
        if (verification != ZoomVerification.Valid)
        {
            // An address over its failed-verification limit is refused without touching the database. The check comes after the (cheap)
            // signature check on purpose: behind a proxy that does not forward the real address every caller shares one address, and a
            // flood of bad requests must not lock Zoom itself out; a validly signed request always gets through.
            if (limiter.IsBlocked(address))
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            limiter.RegisterFailure(address);
            logger.LogWarning("A Zoom webhook failed verification ({Reason})", verification); // never the payload or the headers
            return Results.Unauthorized();
        }

        if (!limiter.TryAdmit())
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        using (document)
        {
            var root = document.RootElement;
            var eventName = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (eventName == "endpoint.url_validation")
            {
                var plain = root.TryGetProperty("payload", out var p) && p.TryGetProperty("plainToken", out var t) ? t.GetString() : null;
                return plain is null
                    ? Results.BadRequest()
                    : Results.Ok(new { plainToken = plain, encryptedToken = verifier.EncryptPlainToken(plain) });
            }

            // Zoom sends no event id: a re-delivery repeats the body byte for byte, so its hash is the event's identity.
            var eventId = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            http.RequestServices.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true); // the platform context, like a job
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (await db.Set<ZoomWebhookReceipt>().AnyAsync(r => r.EventId == eventId, ct))
                {
                    return Results.Ok(); // already processed; reachable only after signature and timestamp passed
                }

                var payload = root.TryGetProperty("payload", out var pl) ? pl : default;
                if (eventName is not null && payload.ValueKind == JsonValueKind.Object)
                {
                    await processor.ProcessAsync(eventName, payload, ct);
                }

                db.Add(new ZoomWebhookReceipt(eventId, DateTime.UtcNow));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "A Zoom webhook event could not be processed and will be retried"); // no payload
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    /// <summary>The body, read with a hard limit; null when it is larger than <see cref="MaxBodyBytes"/>.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

/// <summary>Category for the webhook log lines.</summary>
public sealed class ZoomWebhookLog;
