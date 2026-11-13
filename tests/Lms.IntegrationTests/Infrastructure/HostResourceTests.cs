using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>
/// #466: every <c>WithWebHostBuilder</c> host used to start its own Hangfire server. Their long-lived connections shared one MySqlConnector
/// pool (100) and exhausted it late in the run ("All pooled connections are in use"). Only the shared root host runs a job server.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HostResourceTests(LmsApiFactory factory)
{
    private static bool RunsJobServer(IServiceProvider services) =>
        services.GetServices<IHostedService>().Any(s => s.GetType().Name.Contains("BackgroundJobServer", StringComparison.Ordinal));

    [Fact]
    public void The_shared_root_host_runs_the_job_server()
    {
        Assert.True(RunsJobServer(factory.Services));
    }

    [Fact]
    public void A_derived_host_does_not_start_another_job_server()
    {
        using var derived = factory.WithWebHostBuilder(_ => { });

        Assert.False(RunsJobServer(derived.Services));
    }

    [Fact]
    public void A_derived_host_can_opt_back_in_to_a_job_server()
    {
        using var derived = factory.WithWebHostBuilder(b => b.UseSetting("Jobs:ServerEnabled", "true"));

        Assert.True(RunsJobServer(derived.Services));
    }
}
