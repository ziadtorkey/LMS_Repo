using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MySql;
using Testcontainers.Minio;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Boots the API against throw-away MySQL and MinIO containers.</summary>
public sealed class LmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("lms")
        .WithUsername("lms")
        .WithPassword("lms_test_pw")
        .Build();

    private readonly MinioContainer _minio = new MinioBuilder("chainguard/minio:latest").Build();

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

    /// <summary>A DbContext over the test database acting as the given caller.</summary>
    public LmsDbContext CreateDbContext(ICurrentContext context) =>
        LmsDbContextFactory.Create(_mysql.GetConnectionString(), context);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Lms", _mysql.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", _minio.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<LmsApiFactory>
{
    public const string Name = "api";
}