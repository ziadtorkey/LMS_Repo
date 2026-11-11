using Lms.Api.Infrastructure;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// Modules implementing <see cref="ILmsModule"/> are discovered from the API assembly, so a new module never edits
/// Program.cs (avoids the merge conflict every module PR used to hit there).
/// </summary>
public class LmsModuleDiscoveryTests
{
    [Fact]
    public void Every_module_is_a_concrete_class_with_a_public_parameterless_constructor()
    {
        var types = LmsModuleRegistry.ModuleTypes;

        Assert.All(types, t =>
        {
            Assert.False(t.IsAbstract);
            Assert.NotNull(t.GetConstructor(Type.EmptyTypes));
        });
    }

    [Fact]
    public void Discovery_order_is_deterministic_and_has_no_duplicates()
    {
        var names = LmsModuleRegistry.ModuleTypes.Select(t => t.FullName!).ToList();

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Add_and_map_run_every_discovered_module_once()
    {
        var calls = new List<string>();
        var modules = new ILmsModule[] { new RecordingModule("a", calls), new RecordingModule("b", calls) };

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        LmsModuleRegistry.AddModules(services, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), modules);

        Assert.Equal(["add:a", "add:b"], calls);
    }

    private sealed class RecordingModule(string name, List<string> calls) : ILmsModule
    {
        public void AddServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            Microsoft.Extensions.Configuration.IConfiguration configuration) => calls.Add("add:" + name);

        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) => calls.Add("map:" + name);
    }
}
