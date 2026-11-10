namespace Lms.Domain.Common;

public interface ISoftDelete
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
}