using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Attendance;

/// <summary>
/// Append-only history of a record (FR-ATT-01, L14): the first take, an edit, an edit after the window (reason required) and an approved excuse. Both statuses
/// are kept, so a report can say "as of" a time. Unit fields are the record ones.
/// </summary>
[Audited]
public sealed class AttendanceRecordChange : UnitOwnedEntity
{
    private AttendanceRecordChange()
    {
        Kind = string.Empty;
    }

    public AttendanceRecordChange(AttendanceRecord record, string? fromStatus, string? toStatus, string kind, Guid byUserId, string? reason, DateTime atUtc)
    {
        PlaceLike(record);
        AttendanceRecordId = record.Id;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Kind = kind;
        ByUserId = byUserId;
        Reason = reason;
        AtUtc = atUtc;
    }

    public Guid AttendanceRecordId { get; private set; }

    public string? FromStatus { get; private set; }

    public string? ToStatus { get; private set; }

    /// <summary>An <c>AttendanceChangeKinds</c> value.</summary>
    public string Kind { get; private set; }

    public Guid ByUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime AtUtc { get; private set; }
}
