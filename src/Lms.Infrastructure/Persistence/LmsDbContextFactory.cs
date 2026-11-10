using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lms.Infrastructure.Persistence;

/// <summary>Builds contexts outside DI (tests, tooling) and serves `dotnet ef` at design time.</summary>
public sealed class LmsDbContextFactory : IDesignTimeDbContextFactory<LmsDbContext>
{
    private const string DesignTimeConnection = "Server=localhost;Port=3306;Database=lms;User=lms;Password=lms_dev_pw";

    public static LmsDbContext Create(string connectionString, ICurrentContext context, IClock? clock = null)
    {
        var builder = new DbContextOptionsBuilder<LmsDbContext>();
        builder.UseLmsMySql(connectionString);
        builder.AddInterceptors(new StampingInterceptor(context, clock ?? new SystemClock()));
        return new LmsDbContext(builder.Options, context);
    }

    public LmsDbContext CreateDbContext(string[] args) => Create(DesignTimeConnection, new DesignTimeContext());

    private sealed class DesignTimeContext : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? AssignmentId => null;
        public string? ScopePath => null;
        public bool IsPlatformAdmin => true;
    }
}