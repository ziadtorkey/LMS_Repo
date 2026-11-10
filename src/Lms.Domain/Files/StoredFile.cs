using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Files;

/// <summary>Metadata of an uploaded object; the bytes live in object storage under ObjectKey.</summary>
[Audited]
public sealed class StoredFile : AuditableEntity, IOrgUnitScoped
{
    private StoredFile()
    {
        OrgUnitPath = ObjectKey = FileName = ContentType = string.Empty;
    }

    public StoredFile(
        Guid organizationId, Guid orgUnitId, string orgUnitPath, string fileName, string contentType, long sizeBytes, DateTime nowUtc)
    {
        OrganizationId = organizationId;
        OrgUnitId = orgUnitId;
        OrgUnitPath = orgUnitPath;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ObjectKey = $"{orgUnitId:N}/{nowUtc:yyyy}/{nowUtc:MM}/{Id:N}{Path.GetExtension(fileName).ToLowerInvariant()}";
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string ObjectKey { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
}
