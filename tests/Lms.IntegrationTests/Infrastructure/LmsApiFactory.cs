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
        Current = this;
        await Task.WhenAll(_mysql.StartAsync(), _minio.StartAsync());
        await using var db = CreateDbContext(TestCurrentContext.PlatformAdmin());
        await db.Database.MigrateAsync();
        StartSampler();
    }

    private CancellationTokenSource? _samplerCts;

    /// <summary>Opt-in diagnostic (#466): with <c>LMS_CONN_LOG</c> set, appends the MySQL process list grouped by user and query every 15 s.</summary>
    private void StartSampler()
    {
        var path = Environment.GetEnvironmentVariable("LMS_CONN_LOG");
        if (string.IsNullOrEmpty(path)) return;
        _samplerCts = new CancellationTokenSource();
        var cts = _samplerCts;
        var root = new MySqlConnector.MySqlConnectionStringBuilder(_mysql.GetConnectionString()) { UserID = "root", Password = "lms_test_pw", Pooling = false };
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await using var c = new MySqlConnector.MySqlConnection(root.ConnectionString);
                    await c.OpenAsync();
                    await using var cmd = new MySqlConnector.MySqlCommand(
                        "SELECT USER, COALESCE(DB,'-'), COMMAND, COUNT(*), MAX(TIME), LEFT(COALESCE(INFO,''),70) FROM information_schema.processlist GROUP BY USER, DB, COMMAND, LEFT(COALESCE(INFO,''),70) ORDER BY 4 DESC LIMIT 12", c);
                    await using var r = await cmd.ExecuteReaderAsync();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"== {DateTime.UtcNow:HH:mm:ss}");
                    while (await r.ReadAsync()) sb.AppendLine($"{r.GetString(0)} {r.GetString(1)} {r.GetString(2)} n={r.GetInt64(3)} maxT={r.GetInt64(4)} {r.GetString(5)}");
                    await File.AppendAllTextAsync(path, sb.ToString());
                }
                catch (Exception ex) { await File.AppendAllTextAsync(path, $"sampler error {ex.GetType().Name}: {ex.Message}{Environment.NewLine}"); }
                try { await Task.Delay(15000, cts.Token); } catch (OperationCanceledException) { }
            }
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _samplerCts?.Cancel();
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
    }

    public TestPolicyEvaluator Policies => Services.GetRequiredService<TestPolicyEvaluator>();

    /// <summary>A DbContext over the test database acting as the given caller.</summary>
    public LmsDbContext CreateDbContext(ICurrentContext context) =>
        LmsDbContextFactory.Create(_mysql.GetConnectionString(), context);

    /// <summary>Creates a fresh database with no schema at all and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "lms_" + Guid.NewGuid().ToString("N")[..12];
        var root = new MySqlConnector.MySqlConnectionStringBuilder(_mysql.GetConnectionString()) { UserID = "root", Password = "lms_test_pw" };
        await using (var admin = new MySqlConnector.MySqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new MySqlConnector.MySqlCommand($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new MySqlConnector.MySqlConnectionStringBuilder(root.ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>Creates a fresh, migrated database on the shared MySQL container and returns its connection string.</summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var name = "lms_" + Guid.NewGuid().ToString("N")[..12];
        var root = new MySqlConnector.MySqlConnectionStringBuilder(_mysql.GetConnectionString())
        {
            UserID = "root",
            Password = "lms_test_pw",
        };
        await using (var admin = new MySqlConnector.MySqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new MySqlConnector.MySqlCommand($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4", admin);
            await create.ExecuteNonQueryAsync();
        }

        var cs = new MySqlConnector.MySqlConnectionStringBuilder(root.ConnectionString) { Database = name }.ConnectionString;
        await using var db = LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());
        await db.Database.MigrateAsync();
        return cs;
    }

    /// <summary>
    /// Derived hosts (<c>WithWebHostBuilder</c>) run without a Hangfire server unless a test opts in with <c>Jobs:ServerEnabled=true</c>.
    /// Each server holds several long-lived MySQL connections from one shared 100-connection pool, and about thirty derived hosts live until
    /// the end of the run: together they exhausted the pool (#466). The shared root host keeps its server for the job tests.
    /// </summary>
    public new WebApplicationFactory<Program> WithWebHostBuilder(Action<IWebHostBuilder> configuration)
    {
        var host = base.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jobs:ServerEnabled", "false");
            configuration(builder);
        });
        _derived.Add(host);
        return host;
    }

    private readonly System.Collections.Concurrent.ConcurrentBag<WebApplicationFactory<Program>> _derived = [];

    /// <summary>How many derived hosts are tracked and not yet disposed (for the cleanup guard).</summary>
    internal int TrackedDerivedHosts => _derived.Count;

    /// <summary>The factory the current run shares (one per process); <see cref="DerivedHostCleanupAttribute"/> reaches it from here.</summary>
    internal static LmsApiFactory? Current { get; private set; }

    /// <summary>
    /// Disposes every derived host created so far. A derived host is kept alive by its parent until the parent is disposed, so a test
    /// that does not dispose its own host leaves a whole application (DI container, EF model, caches) in memory for the rest of the run:
    /// about 200 such hosts took a full run past 13 GB, where it slowed to a crawl and CI cancelled it as hung (#466, #618). The api
    /// collection runs its tests one at a time, so after a test every derived host belongs to a finished test.
    /// </summary>
    internal void DisposeDerivedHosts()
    {
        while (_derived.TryTake(out var host))
        {
            try
            {
                host.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // the test disposed it already
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The host runs as Development (auth config expects it); keep the dev seeder out of the shared test database.
        builder.UseSetting("Seed:RunOnStartup", "false");
        builder.UseSetting("Comms:FakeChannels", "true"); // the delivery kernel sends through the in-memory fakes
        builder.UseSetting("Comms:DispatcherEnabled", "false"); // tests run the dispatcher themselves, at a time they choose
        builder.UseSetting("Jobs:SchedulePollIntervalSeconds", "1"); // scheduled jobs are picked up within a second in tests
        builder.UseSetting("ConnectionStrings:Lms", _mysql.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", _minio.GetConnectionString());
        builder.UseSetting("Storage:AccessKey", MinioBuilder.DefaultUsername);
        builder.UseSetting("Storage:SecretKey", MinioBuilder.DefaultPassword);
        builder.UseSetting("Auth:WasmOrigins:0", "http://localhost");
        builder.UseSetting("PackageHost:Origin", "http://127.0.0.1:5999"); // a content origin on another site than the test server (http://localhost)
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.AddSingleton<Lms.Infrastructure.LiveSessions.FakeMeetingProvider>();
            services.AddSingleton<Lms.Domain.LiveSessions.IMeetingProvider>(sp => sp.GetRequiredService<Lms.Infrastructure.LiveSessions.FakeMeetingProvider>());
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