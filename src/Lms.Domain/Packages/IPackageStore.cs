namespace Lms.Domain.Packages;

/// <summary>
/// The store of extracted package files: many small objects under <c>{organizationId}/{packageId}/</c> (M4b plan Task 8a). The
/// key is always built from the two ids and a relative path that the hardening reader has already validated; the content type is
/// assigned by the server from an extension allow-list, never taken from the archive.
/// </summary>
public interface IPackageStore
{
    /// <summary>The prefix of a package: <c>{organizationId}/{packageId}/</c>.</summary>
    static string PrefixOf(Guid organizationId, Guid packageId) => $"{organizationId:N}/{packageId:N}/";

    Task PutAsync(string prefix, string relativePath, string contentType, Stream content, long length, CancellationToken ct = default);

    /// <summary>Opens a stored file; null when it does not exist.</summary>
    Task<PackageFile?> OpenAsync(string prefix, string relativePath, CancellationToken ct = default);

    /// <summary>The relative paths under a prefix (used by tests and the purge).</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct = default);

    /// <summary>Deletes every object under the prefix; returns how many.</summary>
    Task<int> DeletePrefixAsync(string prefix, CancellationToken ct = default);
}

/// <summary>A stored package file: the stream (the caller disposes it), its server-assigned type and size.</summary>
public sealed record PackageFile(Stream Content, string ContentType, long Length);

/// <summary>
/// Hands a freshly uploaded package to the validation job. M4b Task 8b registers the real scheduler; until then the upload stays
/// <c>Uploaded</c> (a no-op registration).
/// </summary>
public interface IPackageValidationScheduler
{
    Task ScheduleAsync(Guid organizationId, Guid packageId, CancellationToken ct = default);
}
