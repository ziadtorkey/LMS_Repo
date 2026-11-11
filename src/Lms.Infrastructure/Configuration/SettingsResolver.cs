using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Configuration;

public sealed class SettingsResolver(LmsDbContext db, ICurrentContext context) : ISettingsResolver
{
    public async Task<ResolvedSetting<T>> ResolveAsync<T>(
        SettingKey<T> key, string orgUnitPath, CancellationToken cancellationToken = default)
    {
        var rows = await LoadAncestorRowsAsync(key.Name, orgUnitPath, cancellationToken);

        // Highest locked row wins; otherwise the deepest row wins. Rows are ordered shallow to deep.
        var winner = rows.FirstOrDefault(r => r.IsLocked) ?? rows.LastOrDefault();
        return winner is null
            ? new ResolvedSetting<T>(key.Default, null, false)
            : new ResolvedSetting<T>(JsonSerializer.Deserialize<T>(winner.ValueJson)!, winner.OrgUnitId, winner.IsLocked);
    }

    public Task SetAsync<T>(
        SettingKey<T> key, Guid orgUnitId, T value, bool isLocked, CancellationToken cancellationToken = default) =>
        SetJsonAsync(key.Name, orgUnitId, JsonSerializer.Serialize(value), isLocked, cancellationToken);

    public async Task SetJsonAsync(
        string name, Guid orgUnitId, string valueJson, bool isLocked, CancellationToken cancellationToken = default)
    {
        // Through the filtered set: the caller can only write inside its own scope.
        var unit = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == orgUnitId, cancellationToken)
            ?? throw new InvalidOperationException("Org unit not found in the current scope.");

        var strictAncestors = OrgUnitPath.AncestorsAndSelf(unit.Path).SkipLast(1).ToList();
        var rows = await LoadRowsAsync(name, strictAncestors, unit.OrganizationId, cancellationToken);
        var locking = rows.FirstOrDefault(r => r.IsLocked);
        if (locking is not null)
        {
            throw new SettingLockedException(locking.OrgUnitId);
        }

        var json = valueJson;
        // IgnoreQueryFilters only to see soft-deleted rows: the unique (OrgUnitId, Key) index still holds them, so
        // setting the key again must restore the old row instead of inserting. The unit was already resolved through
        // the scoped set above, and the client is re-applied explicitly.
        var existing = await db.Set<OrgUnitSetting>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == unit.OrganizationId && x.OrgUnitId == orgUnitId && x.Key == name,
                cancellationToken);
        if (existing is null)
        {
            db.Add(new OrgUnitSetting(unit.OrganizationId, unit.Id, unit.Path, name, json, isLocked));
        }
        else
        {
            if (existing.IsDeleted)
            {
                existing.Restore();
            }

            existing.Change(json, isLocked);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(string name, Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        // Through the filtered sets: only units and rows inside the scope of the caller, and only live rows.
        _ = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == orgUnitId, cancellationToken)
            ?? throw new InvalidOperationException("Org unit not found in the current scope.");
        var row = await db.Set<OrgUnitSetting>().SingleOrDefaultAsync(x => x.OrgUnitId == orgUnitId && x.Key == name, cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.Remove(row); // soft delete
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<List<OrgUnitSetting>> LoadAncestorRowsAsync(string key, string path, CancellationToken ct)
    {
        // A caller with no client (and not a platform admin) sees nothing. Platform admins have no client of
        // their own; ancestor paths consist of globally unique unit ids, so path matching cannot cross clients.
        if (context.OrganizationId is null && !context.IsPlatformAdmin)
        {
            return [];
        }

        return await LoadRowsAsync(key, OrgUnitPath.AncestorsAndSelf(path), context.OrganizationId, ct);
    }

    private Task<List<OrgUnitSetting>> LoadRowsAsync(
        string key, IReadOnlyList<string> paths, Guid? organizationId, CancellationToken ct) =>
        // IgnoreQueryFilters: ancestors lie outside the scope of the caller, so the scope filter would hide them.
        // The soft-delete and client filters are therefore re-applied by hand below.
        db.Set<OrgUnitSetting>().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.Key == key && paths.Contains(x.OrgUnitPath)
                && (organizationId == null || x.OrganizationId == organizationId))
            .OrderBy(x => x.OrgUnitPath.Length)
            .ToListAsync(ct);
}
