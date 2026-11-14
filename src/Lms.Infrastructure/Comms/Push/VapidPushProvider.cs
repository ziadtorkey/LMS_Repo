using System.Net;
using System.Net.Http;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Lms.Domain.Comms;
using Microsoft.Extensions.Options;
using DomainPushSubscription = Lms.Domain.Comms.PushSubscription;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>
/// Web Push over VAPID (RFC 8291 / 8292) through Lib.Net.Http.WebPush (MIT). A 404 or 410 means the subscription is gone; 429 and 5xx are retried (the
/// push service's <c>Retry-After</c> wins); the other 4xx are permanent. Only a code is kept, never the response body or the endpoint.
/// </summary>
public sealed class VapidPushProvider(PushServiceClient client, IOptions<PushOptions> options) : IPushProvider
{
    private readonly PushOptions _options = options.Value;
    private bool _authenticated;

    public string ProviderName => "vapid";

    public string? PublicKey => _options.IsConfigured ? _options.PublicKey : null;

    public async Task<PushResult> SendAsync(PushTarget target, byte[] payload, TimeSpan timeToLive, bool urgent, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return new PushResult(PushResultKind.NotConfigured, "not_configured");
        }

        if (!_authenticated)
        {
            client.DefaultAuthentication = new VapidAuthentication(_options.PublicKey, _options.PrivateKey) { Subject = _options.Subject };
            _authenticated = true;
        }

        var subscription = new Lib.Net.Http.WebPush.PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);
        var message = new PushMessage(System.Text.Encoding.UTF8.GetString(payload))
        {
            TimeToLive = (int)Math.Clamp(timeToLive.TotalSeconds, 0, 2_419_200),
            Urgency = urgent ? PushMessageUrgency.High : PushMessageUrgency.Normal,
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, cancellationToken);
            return new PushResult(PushResultKind.Accepted);
        }
        catch (PushServiceClientException ex)
        {
            return Map(ex.StatusCode, ex.Headers?.RetryAfter?.Delta);
        }
        catch (HttpRequestException)
        {
            return new PushResult(PushResultKind.Transient, "push_network");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PushResult(PushResultKind.Transient, "push_timeout");
        }
    }

    public static PushResult Map(HttpStatusCode status, TimeSpan? retryAfter) => (int)status switch
    {
        404 or 410 => new PushResult(PushResultKind.Gone, "push_gone"),
        429 => new PushResult(PushResultKind.Transient, "push_429", retryAfter),
        >= 500 => new PushResult(PushResultKind.Transient, "push_" + (int)status, retryAfter),
        _ => new PushResult(PushResultKind.Permanent, "push_" + (int)status),
    };
}
