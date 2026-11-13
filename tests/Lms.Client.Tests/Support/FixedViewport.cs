using MudBlazor;
using MudBlazor.Services;

namespace Lms.Client.Tests.Support;

/// <summary>A viewport that always reports one breakpoint, so breakpoint-driven components (MudHidden) can be tested.</summary>
public sealed class FixedViewport(Breakpoint current) : IBrowserViewportService
{
    public ResizeOptions ResizeOptions { get; } = new();

    public async Task SubscribeAsync(IBrowserViewportObserver observer, bool fireImmediately = true)
    {
        if (fireImmediately) await observer.NotifyBrowserViewportChangeAsync(Args(observer.Id));
    }

    public async Task SubscribeAsync(Guid observerId, Action<BrowserViewportEventArgs> lambda, ResizeOptions? options = null, bool fireImmediately = true)
    {
        if (fireImmediately) lambda(Args(observerId));
        await Task.CompletedTask;
    }

    public async Task SubscribeAsync(Guid observerId, Func<BrowserViewportEventArgs, Task> lambda, ResizeOptions? options = null, bool fireImmediately = true)
    {
        if (fireImmediately) await lambda(Args(observerId));
    }

    public Task UnsubscribeAsync(IBrowserViewportObserver observer) => Task.CompletedTask;
    public Task UnsubscribeAsync(Guid observerId) => Task.CompletedTask;
    public Task<bool> IsMediaQueryMatchAsync(string mediaQuery) => Task.FromResult(false);
    public Task<bool> IsBreakpointWithinWindowSizeAsync(Breakpoint breakpoint) => IsBreakpointWithinReferenceSizeAsync(breakpoint, current);
    public Task<Breakpoint> GetCurrentBreakpointAsync() => Task.FromResult(current);
    public Task<BrowserWindowSize> GetCurrentBrowserWindowSizeAsync() => Task.FromResult(new BrowserWindowSize());

    public Task<bool> IsBreakpointWithinReferenceSizeAsync(Breakpoint breakpoint, Breakpoint reference) => Task.FromResult(breakpoint switch
    {
        Breakpoint.Always => true,
        Breakpoint.None => false,
        Breakpoint.SmAndUp => reference >= Breakpoint.Sm && reference <= Breakpoint.Xxl,
        Breakpoint.MdAndUp => reference >= Breakpoint.Md && reference <= Breakpoint.Xxl,
        Breakpoint.LgAndUp => reference >= Breakpoint.Lg && reference <= Breakpoint.Xxl,
        Breakpoint.XlAndUp => reference >= Breakpoint.Xl && reference <= Breakpoint.Xxl,
        Breakpoint.SmAndDown => reference <= Breakpoint.Sm,
        Breakpoint.MdAndDown => reference <= Breakpoint.Md,
        Breakpoint.LgAndDown => reference <= Breakpoint.Lg,
        Breakpoint.XlAndDown => reference <= Breakpoint.Xl,
        _ => breakpoint == reference,
    });

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private BrowserViewportEventArgs Args(Guid id) => new(id, new BrowserWindowSize(), current, isImmediate: true);
}
