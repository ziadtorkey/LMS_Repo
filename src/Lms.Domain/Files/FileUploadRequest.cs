namespace Lms.Domain.Files;

public sealed record FileUploadRequest(string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);
