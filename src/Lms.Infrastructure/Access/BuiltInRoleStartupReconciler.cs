using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Access;

/// <summary>Runs <see cref="BuiltInRoleReconciler"/> at application start with a platform-level context.</summary>
public sealed class BuiltInRoleStartupReconciler(string connectionString, ILogger<BuiltInRoleStartupReconciler> logger)
{
    private sealed class PlatformContext : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? AssignmentId => null;
        public string? ScopePath => null;
        public bool IsPlatformAdmin => true;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = LmsDbContextFactory.Create(connectionString, new PlatformContext());

            // Never run against a schema that is behind (migrations are a deployment step outside Development).
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                logger.LogWarning("Skipping built-in role reconcile: database has pending migrations.");
                return;
            }

            var updated = await new BuiltInRoleReconciler(db).ReconcileAsync(cancellationToken);
            await ReconcilePresetsAsync(db, cancellationToken);
            if (updated > 0)
            {
                logger.LogInformation("Reconciled default permissions of {Count} built-in roles.", updated);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed reconcile must not stop the API; it is retried on the next start.
            logger.LogError(ex, "Built-in role reconcile failed.");
        }
    }

    /// <summary>Adds the grading presets every client should have (M5 Task 4; add-only, so a restart is safe).</summary>
    private static async Task ReconcilePresetsAsync(LmsDbContext db, CancellationToken cancellationToken)
    {
        var reconciler = new Lms.Infrastructure.Grading.Scales.PresetReconciler(db, new Lms.Infrastructure.Grading.Scales.SaudiPresetCatalog());
        var ids = await db.Organizations.IgnoreQueryFilters().Where(o => !o.IsDeleted).Select(o => o.Id).ToListAsync(cancellationToken); // ISOLATION-BYPASS: platform startup lists every client to seed its presets, ids only
        foreach (var id in ids)
        {
            await reconciler.ReconcileAsync(id, cancellationToken);
        }
    }
}