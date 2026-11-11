using Lms.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Imports;

/// <summary>The registered import definitions, found by their kind.</summary>
public sealed class ImportDefinitions(IEnumerable<IImportDefinition> definitions)
{
    private readonly IReadOnlyList<IImportDefinition> _all = definitions.ToList();

    public IReadOnlyList<IImportDefinition> All => _all;

    public IImportDefinition? Find(string kind) =>
        _all.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.Ordinal));
}

public static class ImportsServiceCollectionExtensions
{
    public const string CleanupJobId = "imports-cleanup";

    public static IServiceCollection AddImports(this IServiceCollection services)
    {
        services.AddScoped<ImportDefinitions>();
        services.AddScoped<ImportValidator>();
        services.AddScoped<ImportService>();
        services.AddScoped<ValidateImportJob>();
        services.AddScoped<CommitImportJob>();
        services.AddScoped<ImportCleanupJob>();
        services.AddHostedService<ImportCleanupRegistrar>();
        return services;
    }

    /// <summary>Adds an import kind. Definitions must use the injected <c>LmsDbContext</c>: the commit transaction lives on it.</summary>
    public static IServiceCollection AddImportDefinition<T>(this IServiceCollection services)
        where T : class, IImportDefinition
    {
        services.AddScoped<IImportDefinition, T>();
        return services;
    }
}

/// <summary>Registers the daily clean-up of abandoned batches (03:00, UTC).</summary>
internal sealed class ImportCleanupRegistrar(IServiceScopeFactory scopes, ILogger<ImportCleanupRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>()
                .AddOrUpdateRecurring<ImportCleanupJob>(ImportsServiceCollectionExtensions.CleanupJobId, j => j.RunAsync(), "0 3 * * *");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The imports-cleanup job could not be registered; it is retried at the next start");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
