using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Persistence;

/// <summary>The EF model must always match the migrations: a change to an entity or a convention needs its migration.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModelDriftTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model differs from the last migration snapshot: add a migration, or fix the mapping that drifted.");
    }
}
