using System.Reflection;

namespace Lms.Api.Infrastructure;

/// <summary>
/// A feature module that registers its own services and endpoints. Implement it as a public, non-generic class with a
/// public parameterless constructor in the module's folder (e.g. <c>Features/Calendar/CalendarModule.cs</c>); it is
/// discovered automatically, so new modules never edit Program.cs. One instance is shared by every host in the
/// process, so modules must hold no state.
/// </summary>
public interface ILmsModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);
}

public static class LmsModuleRegistry
{
    /// <summary>The modules of the API assembly (see <see cref="Discover"/>).</summary>
    public static IReadOnlyList<Type> ModuleTypes { get; } = Discover(typeof(LmsModuleRegistry).Assembly);

    private static readonly Lazy<IReadOnlyList<ILmsModule>> Modules = new(() =>
        ModuleTypes.Select(t => (ILmsModule)Activator.CreateInstance(t)!).ToArray());

    /// <summary>Public, concrete, non-generic <see cref="ILmsModule"/> types of an assembly, in ordinal full-name order.</summary>
    public static IReadOnlyList<Type> Discover(Assembly assembly) => assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true, ContainsGenericParameters: false }
                    && typeof(ILmsModule).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToArray();

    public static IServiceCollection AddLmsModules(this IServiceCollection services, IConfiguration configuration) =>
        AddModules(services, configuration, Modules.Value);

    public static IEndpointRouteBuilder MapLmsModules(this IEndpointRouteBuilder app) => MapModules(app, Modules.Value);

    /// <summary>Runs <see cref="ILmsModule.AddServices"/> for the given modules in order.</summary>
    public static IServiceCollection AddModules(
        IServiceCollection services, IConfiguration configuration, IEnumerable<ILmsModule> modules)
    {
        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        return services;
    }

    /// <summary>Runs <see cref="ILmsModule.MapEndpoints"/> for the given modules in order.</summary>
    public static IEndpointRouteBuilder MapModules(IEndpointRouteBuilder app, IEnumerable<ILmsModule> modules)
    {
        foreach (var module in modules)
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
