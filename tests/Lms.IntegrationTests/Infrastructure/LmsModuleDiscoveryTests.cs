using Lms.Api.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// Modules implementing <see cref="ILmsModule"/> are discovered from the API assembly, so a new module never edits
/// Program.cs (avoids the merge conflict every module PR used to hit there).
/// </summary>
public class LmsModuleDiscoveryTests
{
    [Fact]
    public void Discovery_returns_only_public_concrete_non_generic_modules_in_ordinal_name_order()
    {
        var found = LmsModuleRegistry.Discover(typeof(LmsModuleDiscoveryTests).Assembly);

        Assert.Equal([typeof(Fixtures.AModule), typeof(Fixtures.BModule)], found);
    }

    [Fact]
    public void Api_assembly_modules_all_have_a_public_parameterless_constructor()
    {
        Assert.All(LmsModuleRegistry.ModuleTypes, t => Assert.NotNull(t.GetConstructor(Type.EmptyTypes)));
    }

    [Fact]
    public void Add_and_map_run_every_module_once_in_order()
    {
        var calls = new List<string>();
        var modules = new ILmsModule[] { new Recording("a", calls), new Recording("b", calls) };
        var app = WebApplication.CreateBuilder().Build();

        LmsModuleRegistry.AddModules(new ServiceCollection(), new ConfigurationBuilder().Build(), modules);
        LmsModuleRegistry.MapModules(app, modules);

        Assert.Equal(["add:a", "add:b", "map:a", "map:b"], calls);
    }

    private sealed class Recording(string name, List<string> calls) : ILmsModule
    {
        public void AddServices(IServiceCollection services, IConfiguration configuration) => calls.Add("add:" + name);

        public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) => calls.Add("map:" + name);
    }
}
