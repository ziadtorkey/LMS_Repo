namespace Lms.Client.Services;

/// <summary>
/// Tells screen-reader users what an asynchronous action did ("Reply posted"): a page calls <see cref="Announce"/>, the
/// <c>AnnouncerRegion</c> in the layout reads it out through a polite live region. Visible feedback stays on the page; this is only
/// the spoken counterpart.
/// </summary>
public sealed class Announcer
{
    /// <summary>Raised on every announcement, also when the text is the same as the last one.</summary>
    public event Action? Announced;

    public string? Message { get; private set; }

    /// <summary>Counts announcements, so the region can replace its node (a live region is only read again when its content is replaced).</summary>
    public int Count { get; private set; }

    public void Announce(string message)
    {
        Message = message;
        Count++;
        Announced?.Invoke();
    }
}
