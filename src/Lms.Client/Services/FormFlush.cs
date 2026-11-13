using Microsoft.JSInterop;

namespace Lms.Client.Services;

/// <summary>Commits a pending debounced text field before a form saves (see wwwroot/js/form-flush.js).</summary>
public static class FormFlush
{
    public static async Task CommitPendingAsync(IJSRuntime js)
    {
        try { await js.InvokeVoidAsync("lmsFormFlush"); }
        catch (JSDisconnectedException) { }
    }
}
