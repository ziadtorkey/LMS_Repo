namespace Lms.Domain.Common;

/// <summary>
/// A row with "live keys": nullable columns that hold a value only while the row is live, so a unique index over them
/// enforces an invariant among live rows only (MySQL has no partial unique index; NULLs are distinct).
/// <c>StampingInterceptor</c> calls <see cref="ClearLiveKeys"/> when the row is soft-deleted.
/// </summary>
public interface ILiveKeyed
{
    void ClearLiveKeys();
}
