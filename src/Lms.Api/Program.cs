using Lms.Api.Features.Files;
using Lms.Api.Features.Auth;
using Lms.Api.Features.Me;
using Lms.Api.Infrastructure;
using Lms.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}"));

builder.Services.AddScoped<HttpCurrentContext>();
builder.Services.AddScoped<Lms.Domain.Abstractions.ICurrentContext>(sp => sp.GetRequiredService<HttpCurrentContext>());
builder.Services.AddMe();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddLmsAuth(builder.Configuration, builder.Environment);

builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql");

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRequestLocalization();
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

app.MapMeEndpoints();

// Unknown API routes must be 404, not the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;