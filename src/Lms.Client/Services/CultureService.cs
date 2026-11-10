using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lms.Client.Services;

public interface ICultureService
{
    CultureInfo Current { get; }
    bool IsRightToLeft { get; }

    /// <summary>Persists the culture (ar|en) in localStorage and reloads the app so it starts in that culture.</summary>
    Task SetAsync(string culture);
}

/// <summary>
/// The culture is applied at startup by index.html (Blazor.start applicationCulture, reading lms.culture);
/// this service only switches it.
/// </summary>
public sealed class CultureService(IJSRuntime js, NavigationManager navigation) : ICultureService
{
    public const string StorageKey = "lms.culture";
    public static readonly string[] Supported = ["ar", "en"];

    public CultureInfo Current => CultureInfo.CurrentUICulture;
    public bool IsRightToLeft => Current.TextInfo.IsRightToLeft;

    public async Task SetAsync(string culture)
    {
        if (!Supported.Contains(culture)) throw new ArgumentException("Unsupported culture", nameof(culture));
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, culture);
        navigation.NavigateTo(navigation.Uri, forceLoad: true);
    }
}
