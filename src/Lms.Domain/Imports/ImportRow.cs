using Lms.Domain.Common;

namespace Lms.Domain.Imports;

/// <summary>A data row of a batch with its validation errors; removed together with the batch.</summary>
public sealed class ImportRow : Entity, IOrgUnitScoped
{
    private ImportRow()
    {
        OrgUnitPath = ValuesJson = string.Empty;
    }

    public ImportRow(ImportBatch batch, int rowNumber, string valuesJson, string? errorsJson)
    {
        OrganizationId = batch.OrganizationId;
        OrgUnitId = batch.OrgUnitId;
        OrgUnitPath = batch.OrgUnitPath;
        BatchId = batch.Id;
        RowNumber = rowNumber;
        ValuesJson = valuesJson;
        ErrorsJson = errorsJson;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid BatchId { get; private set; }
    public int RowNumber { get; private set; }
    public string ValuesJson { get; private set; }
    public string? ErrorsJson { get; private set; }
}
