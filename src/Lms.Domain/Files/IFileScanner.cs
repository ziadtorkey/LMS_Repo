namespace Lms.Domain.Files;

/// <summary>
/// The antivirus hook (D65): every file that people upload to share (message attachments, excuse attachments) is scanned before it is stored. It is off in v1 (the no-op
/// <see cref="NoOpFileScanner"/>); a real scanner is wired before production. A failed scan stores nothing.
/// </summary>
public interface IFileScanner
{
    /// <summary>True when the content is clean. The stream is read from its start; a seekable stream is rewound afterwards by the caller.</summary>
    Task<bool> IsCleanAsync(string fileName, Stream content, CancellationToken cancellationToken);
}

/// <summary>The v1 default: everything is clean (the hook exists, no engine is attached, D65).</summary>
public sealed class NoOpFileScanner : IFileScanner
{
    public Task<bool> IsCleanAsync(string fileName, Stream content, CancellationToken cancellationToken) => Task.FromResult(true);
}
