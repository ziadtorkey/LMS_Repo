using Lms.Client.Components.Messaging;
using Lms.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Web Push on this browser. Asking for permission happens only inside <see cref="SubscribeAsync"/>, which the page calls from a click (L11); the consent
/// sentence the person agreed to is remembered on this device. The subscription itself goes to the server through <see cref="ICommsApi"/>.
/// </summary>
public interface IPushDevice
{
    Task<PushState> GetStateAsync();

    /// <summary>Asks the browser for permission and subscribes; null when the person says no.</summary>
    Task<SavePushSubscriptionRequest?> SubscribeAsync();

    Task UnsubscribeAsync();
    Task<bool> HasConsentAsync();
    Task RecordConsentAsync();
}

public sealed class PushDevice(IJSRuntime js, IConfiguration configuration) : IPushDevice
{
    private string? PublicKey => configuration["Lms:VapidPublicKey"];

    public async Task<PushState> GetStateAsync()
    {
        if (string.IsNullOrWhiteSpace(PublicKey) || !await js.InvokeAsync<bool>("lmsPush.supported")) return PushState.Unsupported;
        return await js.InvokeAsync<string>("lmsPush.permission") switch
        {
            "denied" => PushState.Denied,
            "granted" when await js.InvokeAsync<bool>("lmsPush.hasSubscription") => PushState.On,
            _ => PushState.Off,
        };
    }

    public async Task<SavePushSubscriptionRequest?> SubscribeAsync()
    {
        var sub = await js.InvokeAsync<PushRegistration?>("lmsPush.subscribe", PublicKey);
        return sub is null ? null : new SavePushSubscriptionRequest(sub.Endpoint, sub.P256dh, sub.Auth, null);
    }

    public async Task UnsubscribeAsync() => await js.InvokeVoidAsync("lmsPush.unsubscribe");

    public async Task<bool> HasConsentAsync() => await js.InvokeAsync<bool>("lmsPush.hasConsent");

    public async Task RecordConsentAsync() => await js.InvokeVoidAsync("lmsPush.recordConsent");

    private sealed record PushRegistration(string Endpoint, string P256dh, string Auth);
}

/// <summary>The push device of the standalone server and of the tests: a state a test sets, a consent flag, and the subscriptions it handed out.</summary>
public sealed class FakePushDevice : IPushDevice
{
    public PushState State { get; set; } = PushState.Off;
    public bool Consent { get; set; }

    /// <summary>What the person answers when the browser asks for permission.</summary>
    public bool Grants { get; set; } = true;

    public int PermissionPrompts { get; private set; }

    public Task<PushState> GetStateAsync() => Task.FromResult(State);

    public Task<SavePushSubscriptionRequest?> SubscribeAsync()
    {
        PermissionPrompts++;
        if (!Grants)
        {
            State = PushState.Denied;
            return Task.FromResult<SavePushSubscriptionRequest?>(null);
        }

        State = PushState.On;
        return Task.FromResult<SavePushSubscriptionRequest?>(new SavePushSubscriptionRequest("https://push.example/" + Guid.NewGuid().ToString("N"), "p256dh", "auth", null));
    }

    public Task UnsubscribeAsync()
    {
        State = PushState.Off;
        return Task.CompletedTask;
    }

    public Task<bool> HasConsentAsync() => Task.FromResult(Consent);

    public Task RecordConsentAsync()
    {
        Consent = true;
        return Task.CompletedTask;
    }
}
