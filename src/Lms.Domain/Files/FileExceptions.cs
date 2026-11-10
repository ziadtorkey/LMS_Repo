namespace Lms.Domain.Files;

/// <summary>The file is larger than the Files.MaxUploadMb setting that applies to the target unit.</summary>
public sealed class FileTooLargeException(long sizeBytes, long maxBytes)
    : InvalidOperationException($"The file is {sizeBytes} bytes; the maximum is {maxBytes} bytes.")
{
    public long SizeBytes { get; } = sizeBytes;
    public long MaxBytes { get; } = maxBytes;
}

/// <summary>The file extension is not in the allowed list (Storage:AllowedExtensions).</summary>
public sealed class FileTypeNotAllowedException(string extension)
    : InvalidOperationException($"Files of type '{extension}' are not allowed.")
{
    public string Extension { get; } = extension;
}

/// <summary>No such file in the caller scope (wrong id, other client, other unit, or deleted).</summary>
public sealed class StoredFileNotFoundException(Guid fileId)
    : InvalidOperationException("The file was not found.")
{
    public Guid FileId { get; } = fileId;
}
