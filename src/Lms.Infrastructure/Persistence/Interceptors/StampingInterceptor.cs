using Lms.Domain.Abstractions;
using Lms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lms.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps tenant and audit columns and turns deletes of soft-deletable entities into updates.
/// Must run before any audit interceptor so that audit entries see the final state.
/// </summary>
public sealed class StampingInterceptor(ICurrentContext context, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        var now = clock.UtcNow;
        db.ChangeTracker.DetectChanges();

        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    StampTenant(entry);
                    if (entry.Entity is AuditableEntity created)
                    {
                        created.StampCreated(now, context.UserId);
                    }

                    break;
                case EntityState.Modified when entry.Entity is AuditableEntity updated:
                    updated.StampUpdated(now, context.UserId);
                    break;
                case EntityState.Deleted when entry.Entity is AuditableEntity deleted:
                    entry.State = EntityState.Modified;
                    deleted.MarkDeleted(now, context.UserId);
                    break;
            }
        }
    }

    private void StampTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned)
        {
            return;
        }

        var property = entry.Property(nameof(ITenantOwned.OrganizationId));
        var current = (Guid)property.CurrentValue!;

        if (current == Guid.Empty)
        {
            property.CurrentValue = context.OrganizationId
                ?? throw new InvalidOperationException(
                    $"Cannot insert {entry.Metadata.ClrType.Name}: no OrganizationId and no active client context.");
        }
        else if (!context.IsPlatformAdmin && current != context.OrganizationId)
        {
            throw new InvalidOperationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} for a different client than the active context.");
        }
    }
}