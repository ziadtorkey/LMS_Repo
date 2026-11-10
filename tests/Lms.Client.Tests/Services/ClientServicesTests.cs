using Bunit;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Services;

public sealed class ClientServicesTests : BunitContext
{
    private static IConfiguration Config(params (string, string)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    private ServiceProvider Build(IConfiguration config)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton(Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>());
        sc.AddSingleton<Microsoft.JSInterop.IJSRuntime>(JSInterop.JSRuntime);
        ClientServices.Register(sc, config, "http://localhost/");
        return sc.BuildServiceProvider();
    }

    [Fact]
    public void Default_config_uses_the_real_api_client_and_oidc()
    {
        using var provider = Build(Config());

        Assert.IsType<LmsApiClient>(provider.GetRequiredService<ILmsApi>());
        Assert.IsNotType<FakeAuthenticationStateProvider>(provider.GetRequiredService<AuthenticationStateProvider>());
    }

    [Fact]
    public void Fake_api_is_enabled_only_by_explicit_UseFakeApi_true()
    {
        using var provider = Build(Config(("Lms:UseFakeApi", "true")));

        Assert.IsType<FakeLmsApi>(provider.GetRequiredService<ILmsApi>());
        Assert.IsType<FakeAuthenticationStateProvider>(provider.GetRequiredService<AuthenticationStateProvider>());
    }
}
