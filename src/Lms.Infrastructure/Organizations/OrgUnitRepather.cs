using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Lms.Infrastructure.Organizations;

/// <summary>
/// Re-paths every unit-scoped table after a move. The tables are discovered from the EF model (every entity with an
/// <c>OrgUnitPath</c> column, plus <c>OrgUnits.Path</c> itself), so tables added by later modules are covered without
/// editing this class; a coverage test asserts that.
/// </summary>
public sealed class OrgUnitRepather(LmsDbContext db) : IOrgUnitRepather
{
    public sealed record Target(Type ClrType, string Table, string PathColumn, string OrganizationColumn, string? DepthColumn);

    public static IReadOnlyList<Target> DiscoverTargets(IModel model)
    {
        var targets = new List<Target>();
        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            // OrgUnit maps its own path to "Path" (OrgUnitPath is a computed alias there).
            var path = entityType.FindProperty("OrgUnitPath") ?? (entityType.ClrType == typeof(OrgUnit) ? entityType.FindProperty("Path") : null);
            var organization = entityType.FindProperty("OrganizationId");
            if (path is null || path.ClrType != typeof(string))
            {
                continue;
            }

            // A path column without a client column cannot be re-pathed safely: fail loudly instead of skipping it.
            if (organization is null)
            {
                throw new InvalidOperationException($"{entityType.ClrType.Name} stores an org unit path but has no OrganizationId; it cannot be re-pathed.");
            }

            var depth = entityType.ClrType == typeof(OrgUnit) ? entityType.FindProperty("Depth") : null;
            targets.Add(new Target(
                entityType.ClrType, table, path.GetColumnName(store)!, organization.GetColumnName(store)!, depth?.GetColumnName(store)));
        }

        return targets;
    }

    public async Task RepathAsync(
        Guid organizationId, string oldPrefix, string newPrefix, int depthDelta, CancellationToken cancellationToken)
    {
        // ISOLATION-BYPASS: raw SQL on purpose (it must reach soft-deleted rows and every unit-scoped table); each
        // statement is restricted to the client explicitly and to paths under the old prefix.
        foreach (var target in DiscoverTargets(db.Model))
        {
            var depth = target.DepthColumn is null ? string.Empty : $", `{target.DepthColumn}` = `{target.DepthColumn}` + {{3}}";
            var sql =
                $"UPDATE `{target.Table}` SET `{target.PathColumn}` = CONCAT({{0}}, SUBSTRING(`{target.PathColumn}`, {{1}} + 1)){depth} " +
                $"WHERE `{target.OrganizationColumn}` = {{2}} AND `{target.PathColumn}` LIKE CONCAT({{4}}, '%')";
            await db.Database.ExecuteSqlRawAsync(
                sql, [newPrefix, oldPrefix.Length, organizationId, depthDelta, oldPrefix], cancellationToken);
        }
    }
}