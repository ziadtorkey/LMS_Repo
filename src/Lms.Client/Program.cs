using Lms.Client;
using Lms.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

ClientServices.Register(builder.Services, builder.Configuration, builder.HostEnvironment.BaseAddress);

var host = builder.Build();
await host.Services.GetRequiredService<ContextStore>().InitializeAsync();
await host.RunAsync();
