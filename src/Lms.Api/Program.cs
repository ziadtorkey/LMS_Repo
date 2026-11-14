using Lms.Api.Features.Files;
using Lms.Api.Features.Settings;
using Lms.Api.Features.Notifications;
using Lms.Api.Features.Account;
using Lms.Api.Features.Auth;
using Lms.Api.Features.Me;
using Lms.Api.Infrastructure;
using Lms.Api.PackageHost;
using Lms.Infrastructure;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<TokenScrubbingEnricher>() // package tokens never reach a log (ADR 0005)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

builder.Services.AddScoped<HttpCurrentContext>();
builder.Services.AddScoped<Lms.Domain.Abstractions.ICurrentContext>(sp => sp.GetRequiredService<HttpCurrentContext>());
builder.Services.AddMe();
builder.Services.AddSettings();
builder.Services.AddNotifications();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
// Feature modules implementing ILmsModule register themselves; new modules don't edit this file.
builder.Services.AddLmsModules(builder.Configuration);
builder.Services.AddLmsAuth(builder.Configuration, builder.Environment);
builder.Services.AddAccount(builder.Configuration);
builder.Services.AddLmsForwardedHeaders(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql");

var app = builder.Build();

// Development only: migrate the local database and load the sample clients (no-op in every other environment).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Seed:RunOnStartup", true))
{
    await app.Services.GetRequiredService<DevSeeder>().SeedAsync();
}

// After the Development seed has migrated the database: existing clients provisioned under an older defaults version
// get the new built-in permissions (add-only, once per role). Skipped while migrations are pending.
await app.Services.GetRequiredService<BuiltInRoleStartupReconciler>().RunAsync();

// First: everything after it (HTTPS redirection, rate limits, audit IPs) sees the real client address and scheme.
app.UseLmsForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UsePackageHost(); // the content host's own pipeline; before the app's headers, cookies and routes
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseRequestLocalization();
app.UseRateLimiter();
app.UseMiddleware<ExternalSchemeResolutionMiddleware>();
app.UseAuthentication();
app.UseMiddleware<CurrentContextMiddleware>();
app.UseAuthorization();

app.MapAuthorizationEndpoints();
app.MapRazorPages();
app.MapHealthChecks("/health");
app.MapFileEndpoints();
foreach (var module in app.Services.GetServices<IEndpointModule>())
{
    module.Map(app);
}
app.MapLmsModules();

app.MapMeEndpoints();
app.MapSettingEndpoints();
app.MapNotificationEndpoints();

// Unknown API routes must be 404, not the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));
// Serves the client's static web assets, including the fingerprinted (content-hashed) files the Blazor import map in
// index.html points at (/js/richtext.<hash>.js, /_framework/blazor.webassembly.<hash>.js). This replaces
// UseBlazorFrameworkFiles + UseStaticFiles: UseBlazorFrameworkFiles runs its own branch without endpoint execution
// and throws 500 for any asset that MapStaticAssets has matched ("request reached the end of the pipeline").
app.MapStaticAssets();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
