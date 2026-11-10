using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Files;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Persistence;

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options, ICurrentContext currentContext)
    : IdentityUserContext<User, Guid>(options)
{
    // Read by the global query filters; EF re-evaluates it per context instance.
    internal ICurrentContext CurrentContext { get; } = currentContext;

    /// <summary>The caller's scope path if well formed ("/" + segments, trailing "/"), otherwise null (= no scope).</summary>
    internal string? ValidScopePath =>
        CurrentContext.ScopePath is { Length: > 1 } p && p[0] == '/' && p[^1] == '/' ? p : null;

    // One DbSet line per entity; modules add their own configuration file under Persistence/Configurations/<Module>/.
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LevelType> LevelTypes => Set<LevelType>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OrgUnitSetting> OrgUnitSettings => Set<OrgUnitSetting>();
    public DbSet<OrgUnitPolicy> OrgUnitPolicies => Set<OrgUnitPolicy>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LmsDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => !t.IsOwned()))
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            var filter = QueryFilterBuilder.Build(entityType.ClrType, this);
            if (filter is not null)
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }
}
