using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MySql;
using Testcontainers.Minio;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Boots the API against throw-away MySQL and MinIO containers.</summary>
public sealed class LmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql@sha256:a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902")
        .WithDatabase("lms")
        .WithUsername("lms")
        .WithPassword("lms_test_pw")
        .Build();

    private readonly MinioContainer _minio = new MinioBuilder("chainguard/minio@sha256:fb893e36fc369d41e92f678b17ec27378fa5bf0576aeef7ac15460c13629ab5a").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _minio.StartAsync());
        await using var db = CreateDbContext(TestCurrentContext.PlatformAdmin());
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
    }

    public TestPolicyEvaluator Policies => Services.GetRequiredService<TestPolicyEvaluator>();

    /// <summary>A DbContext over the test database acting as the given caller.</summary>
    public LmsDbContext CreateDbContext(ICurrentContext context) =>
        LmsDbContextFactory.Create(_mysql.GetConnectionString(), context);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Lms", _mysql.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", _minio.GetConnectionString());
        builder.UseSetting("Auth:WasmOrigins:0", "http://localhost");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.AddSingleton<IEndpointModule, TestEndpoints>();
            services.AddSingleton<TestPolicyEvaluator>();
            services.AddScoped<IPolicyEvaluator>(sp => sp.GetRequiredService<TestPolicyEvaluator>());
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LmsApiFactory>
{
    public const string Name = "api";
}