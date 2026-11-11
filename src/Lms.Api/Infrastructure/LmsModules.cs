namespace Lms.Api.Infrastructure;

/// <summary>
/// A feature module that registers its own services and endpoints. Implement it in the module's folder
/// (e.g. <c>Features/Calendar/CalendarModule.cs</c>) with a public parameterless constructor; it is discovered
/// automatically, so new modules never edit Program.cs.
/// </summary>
public interface ILmsModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);
}

public static class LmsModuleRegistry
{
    /// <summary>Concrete <see cref="ILmsModule"/> types in the API assembly, ordered by full name (deterministic).</summary>
    public static IReadOnlyList<Type> ModuleTypes { get; } = typeof(LmsModuleRegistry).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ILmsModule).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToArray();

    private static readonly Lazy<IReadOnlyList<ILmsModule>> Modules = new(() =>
        ModuleTypes.Select(t => (ILmsModule)Activator.CreateInstance(t)!).ToArray());

    public static IServiceCollection AddLmsModules(this IServiceCollection services, IConfiguration configuration) =>
        AddModules(services, configuration, Modules.Value);

    public static IEndpointRouteBuilder MapLmsModules(this IEndpointRouteBuilder app)
    {
        foreach (var module in Modules.Value)
        {
            module.MapEndpoints(app);
        }

        return app;
    }

    /// <summary>Runs <see cref="ILmsModule.AddServices"/> for the given modules in order (exposed for tests).</summary>
    public static IServiceCollection AddModules(
        IServiceCollection services, IConfiguration configuration, IEnumerable<ILmsModule> modules)
    {
        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        return services;
    }
}
