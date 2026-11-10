using Lms.Client.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Lms.Client.Services;

/// <summary>Feeds MudBlazor's built-in texts (pager tooltips…) from SharedResource so they follow the UI language.</summary>
public sealed class LmsMudLocalizer(IStringLocalizer<SharedResource> localizer) : MudLocalizer
{
    public override LocalizedString this[string key] => localizer[key];
}
