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
}