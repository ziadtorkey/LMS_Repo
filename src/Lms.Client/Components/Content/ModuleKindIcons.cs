using Lms.Shared.Content;
using MudBlazor;

namespace Lms.Client.Components.Content;

/// <summary>The icon of each module kind (one place, so the plan tree, the lesson list and the viewer agree).</summary>
public static class ModuleKindIcons
{
    /// <summary>Shown for a kind this client does not know yet.</summary>
    public const string Fallback = Icons.Material.Rounded.Extension;

    public static string For(string kind) => kind switch
    {
        ModuleKinds.File => Icons.Material.Rounded.Description,
        ModuleKinds.Video => Icons.Material.Rounded.Videocam,
        ModuleKinds.Audio => Icons.Material.Rounded.Headphones,
        ModuleKinds.Page => Icons.Material.Rounded.Article,
        ModuleKinds.Link => Icons.Material.Rounded.Link,
        ModuleKinds.Assignment => Icons.Material.Rounded.Assignment,
        ModuleKinds.Quiz => Icons.Material.Rounded.Quiz,
        ModuleKinds.LibraryResource => Icons.Material.Rounded.LocalLibrary,
        ModuleKinds.LiveSession => Icons.Material.Rounded.VideoCall,
        ModuleKinds.Scorm => Icons.Material.Rounded.Inventory2,
        ModuleKinds.H5p => Icons.Material.Rounded.Interests,
        _ => Fallback,
    };
}
