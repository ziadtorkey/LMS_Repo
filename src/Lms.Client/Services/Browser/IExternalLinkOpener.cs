using Microsoft.JSInterop;

namespace Lms.Client.Services.Browser;

/// <summary>Opens a link in a new tab without giving the new page a handle on this one. Behind an interface so tests can see what was opened.</summary>
public interface IExternalLinkOpener
{
    ValueTask OpenAsync(string url);
}

public sealed class ExternalLinkOpener(IJSRuntime js) : IExternalLinkOpener
{
    public ValueTask OpenAsync(string url) => js.InvokeVoidAsync("open", url, "_blank", "noopener,noreferrer");
}
