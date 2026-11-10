using System.Text.Encodings.Web;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lms.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Adds an AuditEntry to the same SaveChanges (hence the same transaction) for every create/update/delete of an
/// entity type marked [Audited]. Registered after StampingInterceptor, so a soft delete (already turned into an
/// update with IsDeleted = true) is recorded as Deleted.
/// </summary>
public sealed class AuditInterceptor(ICurrentContext context, IClock clock) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Bookkeeping columns written by the stamping interceptor; noise in an audit diff.
    private static readonly HashSet<string> Stamps =
    [
        nameof(AuditableEntity.CreatedAt), nameof(AuditableEntity.CreatedBy), nameof(AuditableEntity.UpdatedAt),
        nameof(AuditableEntity.UpdatedBy), nameof(AuditableEntity.DeletedAt),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        db.ChangeTracker.DetectChanges();
        var now = clock.UtcNow;
        var entries = new List<AuditEntry>();

        foreach (var entry in db.ChangeTracker.Entries().Where(IsAudited).ToList())
        {
            var audit = entry.State switch
            {
                EntityState.Added => Build(entry, AuditAction.Created, null, Snapshot(entry, current: true), now),
                EntityState.Modified => BuildUpdate(entry, now),
                EntityState.Deleted => Build(entry, AuditAction.Deleted, Snapshot(entry, current: false), null, now),
                _ => null,
            };

            if (audit is not null)
            {
                entries.Add(audit);
            }
        }

        db.Set<AuditEntry>().AddRange(entries);
    }

    private static bool IsAuditable(PropertyEntry property) =>
        !Stamps.Contains(property.Metadata.Name)
        && !(property.Metadata.PropertyInfo is { } info && Attribute.IsDefined(info, typeof(AuditIgnoreAttribute), inherit: true));

    private static bool IsAudited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        && Attribute.IsDefined(entry.Metadata.ClrType, typeof(AuditedAttribute), inherit: true);

    private AuditEntry? BuildUpdate(EntityEntry entry, DateTime now)
    {
        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();

        foreach (var property in entry.Properties.Where(p => p.IsModified && IsAuditable(p)))
        {
            if (Equals(property.OriginalValue, property.CurrentValue))
            {
                continue;
            }

            before[property.Metadata.Name] = property.OriginalValue;
            after[property.Metadata.Name] = property.CurrentValue;
        }

        if (after.Count == 0)
        {
            return null;
        }

        // Stamping turns a delete into an update that flips IsDeleted; report it as the delete it is.
        var action = entry.Entity is ISoftDelete { IsDeleted: true } && after.ContainsKey(nameof(ISoftDelete.IsDeleted))
            ? AuditAction.Deleted
            : AuditAction.Updated;

        return Build(entry, action, JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), now);
    }

    private AuditEntry Build(EntityEntry entry, AuditAction action, string? before, string? after, DateTime now)
    {
        var organizationId = entry.Entity is ITenantOwned
            ? (Guid?)entry.Property(nameof(ITenantOwned.OrganizationId)).CurrentValue
            : context.OrganizationId;

        return new AuditEntry(
            organizationId, context.UserId, context.AssignmentId, action,
            entry.Metadata.ClrType.Name, (Guid)entry.Property(nameof(Entity.Id)).CurrentValue!, before, after, now);
    }

    private static string Snapshot(EntityEntry entry, bool current) =>
        JsonSerializer.Serialize(
            entry.Properties
                .Where(IsAuditable)
                .ToDictionary(p => p.Metadata.Name, p => current ? p.CurrentValue : p.OriginalValue),
            Json);
}
