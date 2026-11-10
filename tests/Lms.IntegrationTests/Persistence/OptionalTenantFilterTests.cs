using Lms.Domain.Common;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Persistence;

/// <summary>
/// Evaluates the generated global filter for a test-only entity carrying the IOptionalTenantOwned marker
/// (nullable OrganizationId: null = platform-level row).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OptionalTenantFilterTests(LmsApiFactory factory)
{
    private sealed class PlatformRow : IOptionalTenantOwned
    {
        public Guid? OrganizationId { get; init; }
    }

    private static readonly Guid ClientA = Guid.NewGuid();
    private static readonly Guid ClientB = Guid.NewGuid();

    private List<PlatformRow> Visible(TestCurrentContext context)
    {
        using var db = factory.CreateDbContext(context);
        var predicate = (Func<PlatformRow, bool>)QueryFilterBuilder.Build(typeof(PlatformRow), db)!.Compile();
        PlatformRow[] rows =
        [
            new() { OrganizationId = null }, new() { OrganizationId = ClientA }, new() { OrganizationId = ClientB },
        ];
        return rows.Where(predicate).ToList();
    }

    [Fact]
    public void Null_org_context_sees_no_optional_tenant_rows()
    {
        Assert.Empty(Visible(new TestCurrentContext { UserId = Guid.NewGuid() }));
    }

    [Fact]
    public void Client_context_sees_only_its_own_rows_never_platform_level_rows()
    {
        var visible = Visible(TestCurrentContext.For(ClientA, "/x/"));

        Assert.Equal([ClientA], visible.Select(r => r.OrganizationId));
    }

    [Fact]
    public void Platform_level_rows_visible_only_to_platform_admin()
    {
        var visible = Visible(TestCurrentContext.PlatformAdmin());

        Assert.Equal(3, visible.Count);
        Assert.Contains(visible, r => r.OrganizationId is null);
    }
}