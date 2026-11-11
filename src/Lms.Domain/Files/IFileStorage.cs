namespace Lms.Domain.Files;

public interface IFileStorage
{
    /// <exception cref="FileTypeNotAllowedException"/>
    /// <exception cref="FileTooLargeException"/>
    Task<StoredFile> UploadAsync(FileUploadRequest request, Stream content, CancellationToken cancellationToken = default);

    /// <summary>A presigned GET url (default lifetime 10 minutes).</summary>
    /// <exception cref="StoredFileNotFoundException"/>
    Task<Uri> GetDownloadUrlAsync(Guid fileId, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>The stored bytes (the caller disposes the stream). Used by background jobs that process uploads.</summary>
    /// <exception cref="StoredFileNotFoundException"/>
    Task<Stream> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Soft deletes the record; the object itself is removed later by a retention job.</summary>
    /// <exception cref="StoredFileNotFoundException"/>
    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default);
}
