using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.OrgUnits;

/// <summary>An API host using the real policy evaluator, plus helpers to call the org unit endpoints as an assignment.</summary>
public sealed class OrgUnitTestHost : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public OrgUnitTestHost(LmsApiFactory factory, Action<IServiceCollection>? configure = null)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPolicyEvaluator>();
            services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
            configure?.Invoke(services);
        }));
    }

    public IServiceProvider Services => _host.Services;

    public void Dispose() => _host.Dispose();

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, RoleAssignment as_, object? body = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, as_.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, as_.Id.ToString());
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _host.CreateClient().SendAsync(request);
    }

    public Task<HttpResponseMessage> SendAsPlatformAdminAsync(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _host.CreateClient().SendAsync(request);
    }

    /// <summary>Adds a deeper level type (order 2) to the client so children can be created below the seeded units.</summary>
    public async Task<LevelType> AddDeeperLevelAsync(AccessTree tree)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var level = new LevelType(tree.Org.Id, "مستوى أعمق", "Deeper level", 2, true);
        db.Add(level);
        await db.SaveChangesAsync();
        return level;
    }

    public async Task AddPolicyAsync(AccessTree tree, OrgUnit unit, string policyKey)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitPolicy(tree.Org.Id, unit.Id, unit.Path, policyKey));
        await db.SaveChangesAsync();
    }

    public async Task SetStatusInDbAsync(OrgUnit unit, OrgUnitStatus status)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.OrgUnits.SingleAsync(u => u.Id == unit.Id);
        db.Entry(row).Property(nameof(OrgUnit.Status)).CurrentValue = status;
        await db.SaveChangesAsync();
    }
}