namespace Lms.Domain.Common;

public abstract class AuditableEntity : Entity, ISoftDelete
{
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public void StampCreated(DateTime utcNow, Guid? userId)
    {
        CreatedAt = utcNow;
        CreatedBy = userId;
    }

    public void StampUpdated(DateTime utcNow, Guid? userId)
    {
        UpdatedAt = utcNow;
        UpdatedBy = userId;
    }

    public void MarkDeleted(DateTime utcNow, Guid? userId)
    {
        IsDeleted = true;
        DeletedAt = utcNow;
        StampUpdated(utcNow, userId);
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
    }
}