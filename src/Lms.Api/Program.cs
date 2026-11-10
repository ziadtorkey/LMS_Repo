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
builder.Services.AddAuthentication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql");

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseMiddleware<CurrentContextMiddleware>();

app.MapHealthChecks("/health");
foreach (var module in app.Services.GetServices<IEndpointModule>())
{
    module.Map(app);
}

// Unknown API routes must be 404, not the SPA's index.html.
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found"));
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;