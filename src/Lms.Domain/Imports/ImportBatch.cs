using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Imports;

/// <summary>
/// One uploaded Excel file and what happened to it (X-06). Scoped to the unit the people are imported into.
/// The cell values live in <see cref="ImportRow"/>; neither is audited field by field.
/// </summary>
[Audited]
public sealed class ImportBatch : AuditableEntity, IOrgUnitScoped
{
    private ImportBatch()
    {
        Kind = FileName = Culture = OrgUnitPath = string.Empty;
    }

    public ImportBatch(OrgUnit target, string kind, string fileName, Guid sourceFileId, Guid requestedBy, string culture)
    {
        OrganizationId = target.OrganizationId;
        OrgUnitId = target.Id;
        OrgUnitPath = target.Path;
        Kind = kind;
        FileName = fileName;
        SourceFileId = sourceFileId;
        RequestedBy = requestedBy;
        Culture = culture;
        Status = ImportStatus.Uploaded;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string Kind { get; private set; }
    public ImportStatus Status { get; private set; }
    public Guid SourceFileId { get; private set; }
    public string FileName { get; private set; }
    public int RowCount { get; private set; }
    public int ErrorCount { get; private set; }
    public Guid? ErrorReportFileId { get; private set; }
    public int? CreatedCount { get; private set; }
    public Guid RequestedBy { get; private set; }

    /// <summary>The requester's language: error messages of the report and of the row preview are written in it.</summary>
    public string Culture { get; private set; }

    public DateTime? CommittedAt { get; private set; }

    /// <summary>Why the commit failed (<c>unexpected</c>); null otherwise.</summary>
    public string? FailureCode { get; private set; }

    public void SetStatus(ImportStatus status) => Status = status;

    public void RecordValidation(int rowCount, int errorCount, Guid? errorReportFileId)
    {
        RowCount = rowCount;
        ErrorCount = errorCount;
        ErrorReportFileId = errorReportFileId;
        Status = errorCount == 0 ? ImportStatus.Valid : ImportStatus.Invalid;
    }

    public void MarkCommitted(int createdCount, DateTime nowUtc)
    {
        Status = ImportStatus.Committed;
        CreatedCount = createdCount;
        CommittedAt = nowUtc;
    }

    public void MarkFailed(string failureCode)
    {
        Status = ImportStatus.Failed;
        FailureCode = failureCode;
    }
}
