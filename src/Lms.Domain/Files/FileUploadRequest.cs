namespace Lms.Domain.Files;

/// <summary>ContentType is what the client claimed; it is advisory only and never stored or served.</summary>
public sealed record FileUploadRequest(string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);
