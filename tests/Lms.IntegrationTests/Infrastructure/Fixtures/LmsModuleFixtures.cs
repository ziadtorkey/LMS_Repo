using Lms.Api.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Fixture types for LmsModuleDiscoveryTests: discovery must return exactly AModule and BModule, in that order.
namespace Lms.IntegrationTests.Infrastructure.Fixtures;

public class BModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

public class AModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

public abstract class AbstractModule : ILmsModule
{
    public abstract void AddServices(IServiceCollection services, IConfiguration configuration);

    public abstract void MapEndpoints(IEndpointRouteBuilder app);
}

public class GenericModule<T> : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

internal sealed class InternalModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app) { }
}

public class NotAModule
{
}
