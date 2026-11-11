using Lms.Domain.Abstractions;
using Lms.Domain.Common;
using Lms.Domain.Organizations;
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
                    (deleted as ILiveKeyed)?.ClearLiveKeys();
                    break;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                EnforceScope(entry);
            }
        }
    }

    /// <summary>
    /// Non-platform-admins may not move a row to another client, nor write a unit-scoped row outside their subtree.
    /// </summary>
    private void EnforceScope(EntityEntry entry)
    {
        if (context.IsPlatformAdmin)
        {
            return;
        }

        var name = entry.Metadata.ClrType.Name;

        if (entry.Entity is ITenantOwned && entry.State == EntityState.Modified)
        {
            var org = entry.Property(nameof(ITenantOwned.OrganizationId));
            if (org.IsModified && !Equals(org.OriginalValue, org.CurrentValue))
            {
                throw new InvalidOperationException($"Cannot change the client of an existing {name}.");
            }
        }

        if (entry.Entity is IOrgUnitScoped scoped)
        {
            var scope = context.ScopePath;
            var valid = scope is { Length: > 1 } && scope[0] == '/' && scope[^1] == '/';
            if (!valid || !OrgUnitPath.IsWithin(scoped.OrgUnitPath, scope!))
            {
                throw new InvalidOperationException($"Cannot write {name} outside the active scope.");
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