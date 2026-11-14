using Microsoft.JSInterop;

namespace Lms.Client.Services;

/// <summary>
/// Whether the viewport is a phone (under 600 px). Pages that render ONE layout per size (the gradebook, L19) ask it instead of rendering both and
/// hiding one with CSS, so edits typed before a breakpoint change survive it and only one layout is in the DOM.
/// </summary>
public interface IViewport
{
    /// <summary>True once the first answer arrived (before it a page shows its loading state, so the wrong layout never flashes).</summary>
    bool Ready { get; }

    bool IsPhone { get; }

    event Action? Changed;

    Task EnsureAsync();
}

public sealed class Viewport(IJSRuntime js) : IViewport, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<Viewport>? _reference;

    public bool Ready { get; private set; }

    public bool IsPhone { get; private set; }

    public event Action? Changed;

    public async Task EnsureAsync()
    {
        if (Ready) return;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/viewport.js");
            _reference = DotNetObjectReference.Create(this);
            IsPhone = await _module.InvokeAsync<bool>("watch", _reference);
        }
        catch (JSException)
        {
            IsPhone = false; // no media query support: the desktop layout
        }

        Ready = true;
        Changed?.Invoke();
    }

    [JSInvokable]
    public void OnChanged(bool phone)
    {
        IsPhone = phone;
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _reference?.Dispose();
        return ValueTask.CompletedTask;
    }
}
