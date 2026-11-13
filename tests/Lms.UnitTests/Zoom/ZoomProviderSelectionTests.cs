using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Zoom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomProviderSelectionTests
{
    private sealed class Slots : IHostSlotStore
    {
        public Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HostSlot>>([]);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IMeetingProvider Resolve(string environment, Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new ManualClock(DateTime.UtcNow))
            .AddSingleton<IJobKeyLock>(new InProcessJobKeyLock())
            .AddScoped<IHostSlotStore, Slots>()
            .AddSingleton<IHostEnvironment>(new Env(environment))
            .AddZoom(config);
        var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMeetingProvider>();
    }

    private static readonly Dictionary<string, string?> Configured = new()
    {
        ["Zoom:AccountId"] = "a", ["Zoom:ClientId"] = "c", ["Zoom:ClientSecret"] = "s", ["Zoom:HostPool:0"] = "h1@pool.test",
    };

    [Fact]
    public void Development_without_credentials_uses_the_fake() =>
        Assert.IsType<FakeMeetingProvider>(Resolve("Development", []));

    [Fact]
    public void Production_without_credentials_uses_zoom_which_reports_not_configured() =>
        Assert.IsType<ZoomMeetingProvider>(Resolve("Production", []));

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void With_credentials_zoom_is_used_everywhere(string environment) =>
        Assert.IsType<ZoomMeetingProvider>(Resolve(environment, Configured));

    [Fact]
    public void The_host_share_comes_from_server_configuration_within_its_range()
    {
        Assert.Equal(25, ZoomServiceCollectionExtensions.HostSharePercent(new ConfigurationBuilder().Build()));
        Assert.Equal(40, ZoomServiceCollectionExtensions.HostSharePercent(Config("40")));
        Assert.Equal(1, ZoomServiceCollectionExtensions.HostSharePercent(Config("0")));
        Assert.Equal(100, ZoomServiceCollectionExtensions.HostSharePercent(Config("500")));

        static IConfiguration Config(string value) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Live:MaxHostSharePercent"] = value }).Build();
    }
}
