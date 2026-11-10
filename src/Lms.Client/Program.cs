using Lms.Client;
using Lms.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddLocalization();
builder.Services.AddMudServices();
builder.Services.AddScoped<ICultureService, CultureService>();
builder.Services.AddScoped<ContextStore>();
builder.Services.AddScoped<IContextStore>(sp => sp.GetRequiredService<ContextStore>());

// Only the standalone dev profile ("Standalone" environment, see launchSettings.json) runs on fakes.
// The API-hosted client (Development/CI/Production) always uses the real API + OIDC.
if (builder.HostEnvironment.Environment == "Standalone")
{
    // No backend/OIDC needed to run the shell.
    builder.Services.AddScoped<ILmsApi, FakeLmsApi>();
    builder.Services.AddScoped<AuthenticationStateProvider, FakeAuthenticationStateProvider>();
    builder.Services.AddAuthorizationCore();
}
else
{
    builder.Services.AddOidcAuthentication(options =>
    {
        options.ProviderOptions.Authority = builder.HostEnvironment.BaseAddress.TrimEnd('/');
        options.ProviderOptions.ClientId = "lms-wasm";
        options.ProviderOptions.ResponseType = "code";
        options.ProviderOptions.DefaultScopes.Add("lms_api");
    });
    builder.Services.AddTransient<ForbiddenRedirectHandler>();
    builder.Services.AddHttpClient<ILmsApi, LmsApiClient>(c => c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
        .AddHttpMessageHandler<BaseAddressAuthorizationMessageHandler>()
        .AddHttpMessageHandler<ForbiddenRedirectHandler>();
}

var host = builder.Build();
await host.Services.GetRequiredService<ContextStore>().InitializeAsync();
await host.RunAsync();
