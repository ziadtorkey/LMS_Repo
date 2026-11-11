using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// D15 across the whole M1 surface: an Org Admin assigned at a unit **below** an Inactive unit has no valid context, so
/// every M1 endpoint answers 403 "Invalid context" before any handler runs (never data, never a write). Runs over the
/// same rows as <see cref="M1EndpointMatrixTests"/>; rows whose route is not mapped yet are skipped the same way.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1InactiveUnitProbeTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (LmsApiFactory Factory, RoleAssignment Assignment)? _caller;

    public static TheoryData<string> Rows()
    {
        var data = new TheoryData<string>();
        foreach (var row in M1ProbeRows.All)
        {
            data.Add(row.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Admin_below_an_inactive_unit_gets_invalid_context_on_every_endpoint(string rowName)
    {
        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var world = await M1ProbeWorld.GetAsync(factory);
        var request = await row.Build(world, Caller.RootAdmin);
        using var response = await world.Callers.SendAsAsync(await CallerBelowInactiveUnitAsync(), row.Method, request.Url, request.Body);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == 403, $"{row.Name}: expected 403, got {(int)response.StatusCode}. Body: {body}");
        Assert.Equal("Invalid context", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
    }

    /// <summary>Own client: Root → School1 (Inactive) → Section, with an Org Admin at Section (D15: "or below one").</summary>
    private async Task<RoleAssignment> CallerBelowInactiveUnitAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_caller is { } cached && ReferenceEquals(cached.Factory, factory))
            {
                return cached.Assignment;
            }

            var tree = await AccessTestData.SeedAsync(factory, "M1D15");
            OrgUnit section;
            await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                section = OrgUnit.CreateChild(tree.School1, tree.School1.LevelTypeId, "قسم", "Section D15", "D15S");
                db.Add(section);
                var school = await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id);
                db.Entry(school).Property(nameof(OrgUnit.Status)).CurrentValue = OrgUnitStatus.Inactive;
                await db.SaveChangesAsync();
            }

            var assignment = await AccessTestData.AssignAsync(factory, tree, section, BuiltInRoles.OrgAdmin, Guid.NewGuid());
            _caller = (factory, assignment);
            return assignment;
        }
        finally
        {
            Gate.Release();
        }
    }

    private bool IsMapped(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Any(e => e.RoutePattern.RawText == row.Pattern
                      && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(row.Method.Method) ?? false));
}
